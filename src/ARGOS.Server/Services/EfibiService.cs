using System.Globalization;
using System.Text.Json;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Argos.Server.Services;

public sealed class EfibiOptions
{
    /// <summary>
    /// Connexion MariaDB du serveur FiveM, idéalement avec un utilisateur en lecture seule (GRANT SELECT).
    /// Ex. : « Server=127.0.0.1;Port=3306;Database=qbcore;User ID=argos_lecture;Password=… ».
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    public string Table { get; set; } = "vdms_efibi_records";
}

/// <summary>
/// eFiBi : lecture seule de la table des fiches bilans. Uniquement des SELECT paramétrés ;
/// la connexion est ouverte en lecture seule et chaque consultation de fiche est tracée dans le journal.
/// </summary>
public sealed class EfibiService(IOptionsMonitor<EfibiOptions> options, OdeDatabase db, ILogger<EfibiService> logger)
{
    private const string Columns =
        "id, share_code, citizenid, intervention_number, victim_name, transmission_status, started_at, transmitted_at, created_at, updated_at";

    public bool Configured => !string.IsNullOrWhiteSpace(options.CurrentValue.ConnectionString);

    private string Table
    {
        get
        {
            var table = options.CurrentValue.Table;
            // Nom de table injecté dans la requête : lettres, chiffres et _ uniquement.
            return !string.IsNullOrWhiteSpace(table) && table.All(c => char.IsLetterOrDigit(c) || c == '_') ? table : "vdms_efibi_records";
        }
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var builder = new MySqlConnectionStringBuilder(options.CurrentValue.ConnectionString) { ConnectionTimeout = 5, DefaultCommandTimeout = 15 };
        var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        // Garde-fou : la session elle-même interdit toute écriture.
        await using (var ro = new MySqlCommand("SET SESSION TRANSACTION READ ONLY", connection))
            await ro.ExecuteNonQueryAsync();
        return connection;
    }

    public async Task<EfibiPage> SearchAsync(EfibiQuery q)
    {
        var page = new EfibiPage { Configured = Configured, Page = Math.Max(1, q.Page), PageSize = Math.Clamp(q.PageSize, 5, 100) };
        if (!Configured)
        {
            page.Error = "eFiBi n'est pas encore relié à la base de données du serveur (section Efibi de appsettings.Local.json).";
            return page;
        }
        try
        {
            await using var connection = await OpenAsync();
            var where = new List<string>();
            var parameters = new List<MySqlParameter>();
            void Add(string clause, string name, object value)
            {
                where.Add(clause);
                parameters.Add(new MySqlParameter(name, value));
            }

            if (!string.IsNullOrWhiteSpace(q.Text))
            {
                var t = q.Text.Trim();
                var clause = "(intervention_number LIKE @t OR victim_name LIKE @t OR share_code = @code";
                parameters.Add(new MySqlParameter("@t", "%" + t + "%"));
                parameters.Add(new MySqlParameter("@code", t.ToUpperInvariant()));
                if (long.TryParse(t, out var tid))
                {
                    clause += " OR id = @tid";
                    parameters.Add(new MySqlParameter("@tid", tid));
                }
                where.Add(clause + ")");
            }
            if (!string.IsNullOrWhiteSpace(q.Intervention)) Add("intervention_number LIKE @inter", "@inter", "%" + q.Intervention.Trim() + "%");
            if (!string.IsNullOrWhiteSpace(q.Victim)) Add("victim_name LIKE @victim", "@victim", "%" + q.Victim.Trim() + "%");
            if (q.Id is { } id) Add("id = @id", "@id", id);
            if (!string.IsNullOrWhiteSpace(q.ShareCode)) Add("share_code = @share", "@share", q.ShareCode.Trim().ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(q.Status)) Add("transmission_status = @status", "@status", q.Status);
            if (q.From is { } from) Add("created_at >= @from", "@from", from.UtcDateTime);
            if (q.To is { } to) Add("created_at < @to", "@to", to.UtcDateTime);
            var filter = where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where);

            await using (var count = new MySqlCommand($"SELECT COUNT(*) FROM `{Table}`{filter}", connection))
            {
                count.Parameters.AddRange(parameters.Select(p => p.Clone()).ToArray());
                page.Total = Convert.ToInt32(await count.ExecuteScalarAsync());
            }

            await using (var cmd = new MySqlCommand(
                $"SELECT {Columns} FROM `{Table}`{filter} ORDER BY created_at {(q.OldestFirst ? "ASC" : "DESC")}, id DESC LIMIT @limit OFFSET @offset",
                connection))
            {
                cmd.Parameters.AddRange(parameters.Select(p => p.Clone()).ToArray());
                cmd.Parameters.AddWithValue("@limit", page.PageSize);
                cmd.Parameters.AddWithValue("@offset", (page.Page - 1) * page.PageSize);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    page.Items.Add(ReadSummary(reader));
            }

            await using (var statuses = new MySqlCommand($"SELECT DISTINCT transmission_status FROM `{Table}` ORDER BY 1", connection))
            await using (var reader = await statuses.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    page.Statuses.Add(reader.GetString(0));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Lecture eFiBi impossible : {Message}", ex.Message);
            page.Error = "Base des fiches bilans injoignable : " + ex.Message;
        }
        return page;
    }

    public async Task<EfibiRecord> GetAsync(ArgosUserDto user, long id)
    {
        if (!Configured)
            return new EfibiRecord { Error = "eFiBi n'est pas relié à la base de données." };
        try
        {
            await using var connection = await OpenAsync();
            await using var cmd = new MySqlCommand($"SELECT {Columns}, payload FROM `{Table}` WHERE id = @id", connection);
            cmd.Parameters.AddWithValue("@id", id);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return new EfibiRecord { Error = "Fiche introuvable." };
            var record = new EfibiRecord { Summary = ReadSummary(reader) };
            ParsePayload(reader.IsDBNull(10) ? "{}" : reader.GetString(10), record);

            // Traçabilité des accès aux données sensibles.
            db.AddJournal(new JournalEntry
            {
                At = DateTimeOffset.UtcNow, UserId = user.UserId, UserName = user.DisplayName,
                Action = "Consultation eFiBi", Target = $"Fiche {id}",
                Details = $"Intervention {Or(record.Summary.InterventionNumber)} · code {record.Summary.ShareCode}"
            });
            return record;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Lecture de la fiche eFiBi {Id} impossible : {Message}", id, ex.Message);
            return new EfibiRecord { Error = "Base des fiches bilans injoignable : " + ex.Message };
        }
    }

    private static EfibiSummary ReadSummary(MySqlDataReader r)
    {
        static DateTimeOffset? Date(MySqlDataReader r, int i)
            => r.IsDBNull(i) ? null : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc));
        return new EfibiSummary
        {
            Id = Convert.ToInt64(r.GetValue(0)),
            ShareCode = r.IsDBNull(1) ? string.Empty : r.GetString(1),
            Author = r.IsDBNull(2) ? string.Empty : r.GetString(2),
            InterventionNumber = r.IsDBNull(3) ? string.Empty : r.GetString(3),
            VictimName = r.IsDBNull(4) ? string.Empty : r.GetString(4),
            TransmissionStatus = r.IsDBNull(5) ? string.Empty : r.GetString(5),
            StartedAt = Date(r, 6),
            TransmittedAt = Date(r, 7),
            CreatedAt = Date(r, 8) ?? DateTimeOffset.MinValue,
            UpdatedAt = Date(r, 9) ?? DateTimeOffset.MinValue
        };
    }

    /// <summary>
    /// Lit le formulaire enregistré par le script eFiBi : sections { title, controls[label, value, type], lists, choices }.
    /// Les cases non cochées sont omises ; un champ vide s'affiche « Non renseigné ».
    /// </summary>
    public static void ParsePayload(string json, EfibiRecord record)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
                foreach (var p in meta.EnumerateObject())
                    record.Metadata[p.Name] = Text(p.Value);

            if (!root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Object)
                return;
            foreach (var s in sections.EnumerateObject().OrderBy(s => Order(s.Name)))
            {
                var section = new EfibiSection
                {
                    Key = s.Name,
                    Title = s.Value.TryGetProperty("title", out var title) ? Text(title) : s.Name
                };
                if (s.Value.TryGetProperty("controls", out var controls) && controls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in controls.EnumerateArray().OrderBy(c => c.TryGetProperty("index", out var i) && i.TryGetInt32(out var n) ? n : 0))
                    {
                        var label = c.TryGetProperty("label", out var l) ? Text(l).Trim().TrimEnd(':').Trim() : string.Empty;
                        var type = c.TryGetProperty("type", out var t) ? Text(t) : "text";
                        var value = c.TryGetProperty("value", out var v) ? v : default;
                        if (type is "checkbox" or "radio")
                        {
                            var on = value.ValueKind == JsonValueKind.True
                                     || (value.ValueKind == JsonValueKind.String && value.GetString() is "true" or "on" or "1");
                            if (on) section.Fields.Add(new EfibiField { Label = label, Value = "✔" });
                            continue;
                        }
                        var text = value.ValueKind == JsonValueKind.Undefined ? string.Empty : Text(value).Trim();
                        section.Fields.Add(new EfibiField { Label = label, Value = text.Length == 0 ? EfibiText.NonRenseigne : text });
                    }
                }
                if (s.Value.TryGetProperty("lists", out var lists) && lists.ValueKind == JsonValueKind.Object)
                    foreach (var l in lists.EnumerateObject())
                        section.Lists.Add(new EfibiList
                        {
                            Name = EfibiText.ListName(l.Name),
                            Items = l.Value.ValueKind == JsonValueKind.Array
                                ? l.Value.EnumerateArray().Select(Item).Where(x => x.Length > 0).ToList()
                                : []
                        });
                if (s.Value.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
                {
                    var items = choices.EnumerateArray().Select(Item).Where(x => x.Length > 0).ToList();
                    if (items.Count > 0) section.Lists.Add(new EfibiList { Name = "Sélections", Items = items });
                }
                record.Sections.Add(section);
            }
        }
        catch (JsonException)
        {
            record.Error = "Contenu de la fiche illisible.";
        }
    }

    private static int Order(string key)
    {
        var index = EfibiText.SectionOrder.ToList().IndexOf(key);
        return index < 0 ? 100 : index;
    }

    private static string Item(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => string.Join(" - ", e.EnumerateObject().Select(p => Text(p.Value)).Where(s => s.Length > 0)),
        _ => Text(e)
    };

    private static string Text(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? string.Empty,
        JsonValueKind.Number => e.ToString(),
        JsonValueKind.True => "Oui",
        JsonValueKind.False => "Non",
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => e.ToString()
    };

    private static string Or(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v;
}
