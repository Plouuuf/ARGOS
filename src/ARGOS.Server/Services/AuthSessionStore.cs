using System.Collections.Concurrent;
using System.Security.Cryptography;
using Argos.Shared.Contracts;

namespace Argos.Server.Services;

/// <summary>
/// Sessions en mémoire : connexions Discord en cours (5 min) et jetons d'application actifs.
/// Les jetons survivent aux redémarrages grâce à <see cref="PersistentDiscordSessionStore"/>.
/// </summary>
public sealed class AuthSessionStore
{
    private readonly ConcurrentDictionary<string, PendingSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AppSession> _tokens = new(StringComparer.Ordinal);

    public PendingSession CreateSession()
    {
        Cleanup();
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var session = new PendingSession(id, DateTimeOffset.UtcNow.AddMinutes(5));
        _sessions[id] = session;
        return session;
    }

    public bool TryGetSession(string id, out PendingSession session)
        => _sessions.TryGetValue(id, out session!);

    public string Approve(string id, ArgosUserDto user, TimeSpan lifetime)
    {
        if (!_sessions.TryGetValue(id, out var session))
            throw new InvalidOperationException("Session OAuth inconnue.");

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        session.Approve(user, token);
        _tokens[token] = new AppSession(token, user, DateTimeOffset.UtcNow.Add(lifetime));
        return token;
    }

    public void Restore(string token, ArgosUserDto user, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        _tokens[token] = new AppSession(token, user, DateTimeOffset.UtcNow.Add(lifetime));
    }

    public void Revoke(string token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            _tokens.TryRemove(token, out _);
    }

    public void Deny(string id, string reason)
    {
        if (_sessions.TryGetValue(id, out var session)) session.Deny(reason);
    }

    public bool ValidateToken(string? token, out AppSession session)
    {
        Cleanup();
        if (!string.IsNullOrWhiteSpace(token) &&
            _tokens.TryGetValue(token, out session!) &&
            session.ExpiresAt > DateTimeOffset.UtcNow)
            return true;

        session = null!;
        return false;
    }

    private void Cleanup()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _sessions)
            if (pair.Value.ExpiresAt <= now) _sessions.TryRemove(pair.Key, out _);
        foreach (var pair in _tokens)
            if (pair.Value.ExpiresAt <= now) _tokens.TryRemove(pair.Key, out _);
    }

    public sealed class PendingSession(string id, DateTimeOffset expiresAt)
    {
        private readonly object _sync = new();
        public string Id { get; } = id;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public string Status { get; private set; } = "pending";
        public string? Error { get; private set; }
        public string? AppToken { get; private set; }
        public ArgosUserDto? User { get; private set; }

        public void Approve(ArgosUserDto user, string token)
        {
            lock (_sync)
            {
                if (Status != "pending") return;
                Status = "approved";
                User = user;
                AppToken = token;
            }
        }

        public void Deny(string reason)
        {
            lock (_sync)
            {
                if (Status != "pending") return;
                Status = "denied";
                Error = reason;
            }
        }
    }

    public sealed record AppSession(string Token, ArgosUserDto User, DateTimeOffset ExpiresAt);
}
