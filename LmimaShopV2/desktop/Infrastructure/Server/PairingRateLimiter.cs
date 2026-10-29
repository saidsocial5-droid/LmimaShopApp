using System.Collections.Concurrent;

namespace LmimaShopV2.Desktop.Infrastructure.Server;

/// <summary>Sliding-window rate limiter: five pairing attempts per minute per IP address.</summary>
public sealed class PairingRateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _limit;
    private readonly TimeSpan _window;

    public PairingRateLimiter(int limit = 5, TimeSpan? window = null)
    {
        _limit = limit;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    public bool TryAcquire(string ipAddress)
    {
        var queue = _attempts.GetOrAdd(ipAddress, _ => new Queue<DateTime>());
        lock (queue)
        {
            var cutoff = DateTime.UtcNow - _window;
            while (queue.Count > 0 && queue.Peek() < cutoff) queue.Dequeue();
            if (queue.Count >= _limit) return false;
            queue.Enqueue(DateTime.UtcNow);
            return true;
        }
    }
}
