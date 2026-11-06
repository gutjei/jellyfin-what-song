using System.Collections.Concurrent;

namespace Jellyfin.Plugin.SongPause;

// A short lease avoids duplicate notifications without guessing client/player names.
public sealed class WebClientRegistry
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> leases = new();

    public void Register(string sessionId)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in leases)
            if (pair.Value <= now)
                ((ICollection<KeyValuePair<string, DateTimeOffset>>)leases).Remove(pair);
        leases[sessionId] = now.AddSeconds(90);
    }

    public bool IsActive(string sessionId) => leases.TryGetValue(sessionId, out var until) && until > DateTimeOffset.UtcNow;
}
