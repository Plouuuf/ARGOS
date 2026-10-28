using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Argos.Server.Services;

/// <summary>
/// Sessions Discord enregistrées dans data/discord-sessions.json : seul le hachage du jeton
/// d'application est conservé, et le jeton de rafraîchissement Discord est chiffré (AES-GCM).
/// Repris de SIMCALL, avec une clé propre à ARGOS.
/// </summary>
public sealed class PersistentDiscordSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly string _path;
    private readonly IOptionsMonitor<ArgosServerOptions> _serverOptions;
    private Dictionary<string, StoredSession> _sessions = new(StringComparer.Ordinal);

    public PersistentDiscordSessionStore(IOptionsMonitor<ArgosServerOptions> serverOptions)
    {
        _serverOptions = serverOptions;
        var dir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "discord-sessions.json");
        Load();
    }

    public void Save(string appToken, string userId, string displayName, string refreshToken)
    {
        var tokenHash = HashToken(appToken);
        var record = new StoredSession
        {
            TokenHash = tokenHash,
            UserId = userId,
            DisplayName = displayName,
            EncryptedRefreshToken = Encrypt(refreshToken),
            CreatedAt = DateTimeOffset.UtcNow,
            LastValidatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(180)
        };

        lock (_sync)
        {
            _sessions[tokenHash] = record;
            PersistLocked();
        }
    }

    public bool TryGet(string appToken, out StoredSession session)
    {
        var hash = HashToken(appToken);
        lock (_sync)
        {
            if (_sessions.TryGetValue(hash, out session!) && session.ExpiresAt > DateTimeOffset.UtcNow)
                return true;
            if (_sessions.Remove(hash)) PersistLocked();
            session = null!;
            return false;
        }
    }

    public string GetRefreshToken(StoredSession session) => Decrypt(session.EncryptedRefreshToken);

    public void MarkValidated(string appToken, string displayName, string refreshToken)
    {
        var hash = HashToken(appToken);
        lock (_sync)
        {
            if (!_sessions.TryGetValue(hash, out var current)) return;
            current.DisplayName = displayName;
            current.EncryptedRefreshToken = Encrypt(refreshToken);
            current.LastValidatedAt = DateTimeOffset.UtcNow;
            current.ExpiresAt = DateTimeOffset.UtcNow.AddDays(180);
            PersistLocked();
        }
    }

    public void Revoke(string appToken)
    {
        var hash = HashToken(appToken);
        lock (_sync)
        {
            if (_sessions.Remove(hash)) PersistLocked();
        }
    }

    private void Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path)) return;
            try
            {
                var data = JsonSerializer.Deserialize<List<StoredSession>>(File.ReadAllText(_path), JsonOptions) ?? [];
                _sessions = data
                    .Where(x => !string.IsNullOrWhiteSpace(x.TokenHash) && x.ExpiresAt > DateTimeOffset.UtcNow)
                    .ToDictionary(x => x.TokenHash, StringComparer.Ordinal);
            }
            catch
            {
                _sessions = new(StringComparer.Ordinal);
            }
        }
    }

    private void PersistLocked()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_sessions.Values.OrderBy(x => x.CreatedAt), JsonOptions));
        File.Move(temp, _path, true);
    }

    private string Encrypt(string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(GetEncryptionKey(), 16);
        aes.Encrypt(nonce, plain, cipher, tag);

        var blob = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, blob, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, blob, nonce.Length + tag.Length, cipher.Length);
        return Convert.ToBase64String(blob);
    }

    private string Decrypt(string encoded)
    {
        var blob = Convert.FromBase64String(encoded);
        if (blob.Length < 28) throw new InvalidDataException("Jeton Discord persistant invalide.");
        var nonce = blob.AsSpan(0, 12).ToArray();
        var tag = blob.AsSpan(12, 16).ToArray();
        var cipher = blob.AsSpan(28).ToArray();
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(GetEncryptionKey(), 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private byte[] GetEncryptionKey()
    {
        var accessKey = _serverOptions.CurrentValue.AccessKey ?? string.Empty;
        return SHA256.HashData(Encoding.UTF8.GetBytes(accessKey + "|discord-refresh|argos"));
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public sealed class StoredSession
    {
        public string TokenHash { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string EncryptedRefreshToken { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastValidatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
