using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using CodeMaze.Server.Data.Entities;

namespace CodeMaze.Server.Services;

/// <summary>In-memory session store: session guid -> Player. Singleton, thread-safe.</summary>
public sealed class PlayerSessionStore
{
    private readonly ConcurrentDictionary<Guid, Player> _sessions = new();

    public Guid Create(Player player)
    {
        var key = Guid.NewGuid();
        _sessions[key] = player;
        return key;
    }

    public bool TryGet(Guid key, [NotNullWhen(true)] out Player? player) =>
        _sessions.TryGetValue(key, out player);

    public bool Remove(Guid key) => _sessions.TryRemove(key, out _);
}