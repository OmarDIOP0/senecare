using System.Collections.Concurrent;
using SeneCare.Application.Technicians;

namespace SeneCare.Infrastructure.Realtime;

/// <summary>
/// Technician positions « en route », in memory only (privacy: never written to the database).
/// A position older than 30 minutes is forgotten. Several servers would need a shared store (Redis).
/// </summary>
public sealed class MemoryLivePositions(TimeProvider clock) : ILivePositions
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, LivePosition> _positions = new();

    public void Set(Guid requestId, LivePosition position) => _positions[requestId] = position;

    public LivePosition? Get(Guid requestId)
    {
        if (!_positions.TryGetValue(requestId, out var position))
        {
            return null;
        }

        if (clock.GetUtcNow() - position.At <= MaxAge)
        {
            return position;
        }

        _positions.TryRemove(requestId, out _);
        return null;
    }

    public void Clear(Guid requestId) => _positions.TryRemove(requestId, out _);
}
