using System.Text.Json;
using Argos.Shared.Contracts;
using Microsoft.Data.Sqlite;

namespace Argos.Server.Services;

/// <summary>
/// Base de données d'ARGOS (SQLite, data/argos.db) : rien à installer sur le VPS.
/// Chaque objet métier est rangé en JSON dans sa table ; le journal d'activité est en ajout seul.
/// </summary>
public sealed class OdeDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    /// <param name="path">Fichier de la base ; par défaut data/argos.db à côté du serveur.</param>
    public OdeDatabase(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "data", "argos.db");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        Initialize();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var c = Open();
        Execute(c, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS calls (
                id TEXT PRIMARY KEY, data TEXT NOT NULL, received_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS interventions (
                id TEXT PRIMARY KEY, number TEXT NOT NULL UNIQUE, data TEXT NOT NULL,
                active INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS engins (
                nom TEXT PRIMARY KEY, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS journal (
                id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, user_id TEXT NOT NULL, user_name TEXT NOT NULL,
                action TEXT NOT NULL, target TEXT NOT NULL, details TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS counters (
                name TEXT PRIMARY KEY, value INTEGER NOT NULL);
            -- ADAGIO REMISE : un document JSON par objet, daté pour le tri.
            CREATE TABLE IF NOT EXISTS remise_engins (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS remise_items (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS remise_verifications (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS remise_maintenances (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS remise_movements (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS mco_reports (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sigtao_military (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sigtao_accounts (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sigtao_reports (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ilias_categories (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ilias_documents (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ilias_formations (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ilias_quizzes (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ilias_progress (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ilias_results (id TEXT PRIMARY KEY, at TEXT NOT NULL, data TEXT NOT NULL);
            -- Les mouvements de stock ne peuvent être ni modifiés ni effacés.
            CREATE TRIGGER IF NOT EXISTS movements_no_update BEFORE UPDATE ON remise_movements
                BEGIN SELECT RAISE(ABORT, 'mouvements en lecture seule'); END;
            CREATE TRIGGER IF NOT EXISTS movements_no_delete BEFORE DELETE ON remise_movements
                BEGIN SELECT RAISE(ABORT, 'mouvements en lecture seule'); END;
            -- Le journal d'activité ne peut être ni modifié ni effacé.
            CREATE TRIGGER IF NOT EXISTS journal_no_update BEFORE UPDATE ON journal
                BEGIN SELECT RAISE(ABORT, 'journal en lecture seule'); END;
            CREATE TRIGGER IF NOT EXISTS journal_no_delete BEFORE DELETE ON journal
                BEGIN SELECT RAISE(ABORT, 'journal en lecture seule'); END;
            """);
    }

    private static void Execute(SqliteConnection c, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("O");

    // ===== Compteurs (numéros d'appel et d'intervention) =====

    public long NextCounter(string name)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO counters(name, value) VALUES ($n, 1)
            ON CONFLICT(name) DO UPDATE SET value = value + 1;
            SELECT value FROM counters WHERE name = $n;
            """;
        cmd.Parameters.AddWithValue("$n", name);
        var value = Convert.ToInt64(cmd.ExecuteScalar());
        tx.Commit();
        return value;
    }

    public bool InterventionNumberExists(string number)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM interventions WHERE number = $n";
        cmd.Parameters.AddWithValue("$n", number);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    // ===== Appels =====

    public void SaveCall(CallRecord call)
    {
        using var c = Open();
        Execute(c, """
            INSERT INTO calls(id, data, received_at, updated_at) VALUES ($id, $data, $r, $u)
            ON CONFLICT(id) DO UPDATE SET data = excluded.data, updated_at = excluded.updated_at;
            """,
            ("$id", call.Id), ("$data", JsonSerializer.Serialize(call, JsonOptions)),
            ("$r", Iso(call.ReceivedAt)), ("$u", Iso(call.UpdatedAt)));
    }

    public CallRecord? GetCall(string id)
        => QueryOne<CallRecord>("SELECT data FROM calls WHERE id = $id", ("$id", id));

    public List<CallRecord> RecentCalls(int limit)
        => Query<CallRecord>("SELECT data FROM calls ORDER BY received_at DESC LIMIT $l", ("$l", limit));

    public CallRecord? FindCallBySimcallId(string simcallId)
        => RecentCalls(200).FirstOrDefault(c => c.SimcallCallId == simcallId);

    // ===== Interventions =====

    public void SaveIntervention(Intervention intervention)
    {
        using var c = Open();
        Execute(c, """
            INSERT INTO interventions(id, number, data, active, created_at, updated_at) VALUES ($id, $n, $data, $a, $c, $u)
            ON CONFLICT(id) DO UPDATE SET data = excluded.data, active = excluded.active, updated_at = excluded.updated_at;
            """,
            ("$id", intervention.Id), ("$n", intervention.Number),
            ("$data", JsonSerializer.Serialize(intervention, JsonOptions)),
            ("$a", intervention.Active ? 1 : 0), ("$c", Iso(intervention.CreatedAt)), ("$u", Iso(intervention.UpdatedAt)));
    }

    public Intervention? GetIntervention(string id)
        => QueryOne<Intervention>("SELECT data FROM interventions WHERE id = $id", ("$id", id));

    /// <summary>Interventions actives, plus les plus récentes clôturées (archives).</summary>
    public List<Intervention> Interventions(int closedLimit)
        => Query<Intervention>("""
            SELECT data FROM interventions WHERE active = 1
            UNION ALL
            SELECT data FROM (SELECT data FROM interventions WHERE active = 0 ORDER BY created_at DESC LIMIT $l)
            """, ("$l", closedLimit));

    // ===== Engins =====

    public void SaveEngin(EnginState state)
    {
        using var c = Open();
        Execute(c, """
            INSERT INTO engins(nom, data) VALUES ($n, $d)
            ON CONFLICT(nom) DO UPDATE SET data = excluded.data;
            """, ("$n", state.Nom), ("$d", JsonSerializer.Serialize(state, JsonOptions)));
    }

    public List<EnginState> Engins() => Query<EnginState>("SELECT data FROM engins");

    /// <summary>Renomme l'état ODE d'un engin (changement d'indicatif dans REMISE).</summary>
    public void RenameEngin(string oldNom, EnginState state)
    {
        using var c = Open();
        Execute(c, "DELETE FROM engins WHERE nom = $o", ("$o", oldNom));
        SaveEngin(state);
    }

    // ===== ADAGIO REMISE (documents JSON) =====

    public static class RemiseTables
    {
        public const string Engins = "remise_engins";
        public const string Items = "remise_items";
        public const string Verifications = "remise_verifications";
        public const string Maintenances = "remise_maintenances";
        public const string Movements = "remise_movements";
        public const string Mco = "mco_reports";
        public const string Military = "sigtao_military";
        public const string Accounts = "sigtao_accounts";
        public const string CrReports = "sigtao_reports";
        public const string IliasCategories = "ilias_categories";
        public const string IliasDocuments = "ilias_documents";
        public const string IliasFormations = "ilias_formations";
        public const string IliasQuizzes = "ilias_quizzes";
        public const string IliasProgress = "ilias_progress";
        public const string IliasResults = "ilias_results";
    }

    public void SaveDoc(string table, string id, DateTimeOffset at, object document)
    {
        using var c = Open();
        Execute(c, $"INSERT INTO {table}(id, at, data) VALUES ($id, $at, $d) ON CONFLICT(id) DO UPDATE SET at = excluded.at, data = excluded.data",
            ("$id", id), ("$at", Iso(at)), ("$d", JsonSerializer.Serialize(document, JsonOptions)));
    }

    /// <summary>Ajout seul (mouvements de stock) : refusé si l'identifiant existe déjà.</summary>
    public void InsertDoc(string table, string id, DateTimeOffset at, object document)
    {
        using var c = Open();
        Execute(c, $"INSERT INTO {table}(id, at, data) VALUES ($id, $at, $d)",
            ("$id", id), ("$at", Iso(at)), ("$d", JsonSerializer.Serialize(document, JsonOptions)));
    }

    public void DeleteDoc(string table, string id)
    {
        using var c = Open();
        Execute(c, $"DELETE FROM {table} WHERE id = $id", ("$id", id));
    }

    public T? GetDoc<T>(string table, string id) where T : class
        => QueryOne<T>($"SELECT data FROM {table} WHERE id = $id", ("$id", id));

    public List<T> Docs<T>(string table, int limit = 100000)
        => Query<T>($"SELECT data FROM {table} ORDER BY at DESC LIMIT $l", ("$l", limit));

    public bool AnyDoc(string table)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT EXISTS(SELECT 1 FROM {table})";
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    // ===== Journal =====

    public void AddJournal(JournalEntry entry)
    {
        using var c = Open();
        Execute(c, """
            INSERT INTO journal(at, user_id, user_name, action, target, details) VALUES ($at, $uid, $un, $a, $t, $d)
            """,
            ("$at", Iso(entry.At)), ("$uid", entry.UserId), ("$un", entry.UserName),
            ("$a", entry.Action), ("$t", entry.Target), ("$d", entry.Details));
    }

    public List<JournalEntry> Journal(int limit)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, at, user_id, user_name, action, target, details FROM journal ORDER BY id DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$l", limit);
        using var reader = cmd.ExecuteReader();
        var list = new List<JournalEntry>();
        while (reader.Read())
        {
            list.Add(new JournalEntry
            {
                Id = reader.GetInt64(0),
                At = DateTimeOffset.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                UserId = reader.GetString(2),
                UserName = reader.GetString(3),
                Action = reader.GetString(4),
                Target = reader.GetString(5),
                Details = reader.GetString(6)
            });
        }
        return list;
    }

    // ===== Outils =====

    private List<T> Query<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        var list = new List<T>();
        while (reader.Read())
        {
            var item = JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions);
            if (item is not null)
                list.Add(item);
        }
        return list;
    }

    private T? QueryOne<T>(string sql, params (string Name, object? Value)[] parameters) where T : class
        => Query<T>(sql, parameters).FirstOrDefault();
}
