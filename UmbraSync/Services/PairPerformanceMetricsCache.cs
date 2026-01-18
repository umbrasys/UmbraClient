using System.Collections.Concurrent;

namespace UmbraSync.Services;

public sealed class PairPerformanceMetricsCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    
    public bool TryGetMetrics(string ident, string dataHash, out PairPerformanceMetrics metrics)
    {
        metrics = default;

        if (string.IsNullOrEmpty(ident) || string.IsNullOrEmpty(dataHash))
            return false;

        var key = MakeKey(ident, dataHash);
        if (_cache.TryGetValue(key, out var entry))
        {
            metrics = entry.Metrics;
            entry.LastAccessedUtc = DateTime.UtcNow;
            return true;
        }

        return false;
    }
    
    public void StoreMetrics(string ident, string dataHash, PairPerformanceMetrics metrics)
    {
        if (string.IsNullOrEmpty(ident) || string.IsNullOrEmpty(dataHash))
            return;

        var key = MakeKey(ident, dataHash);
        _cache[key] = new CacheEntry
        {
            Metrics = metrics,
            CreatedUtc = DateTime.UtcNow,
            LastAccessedUtc = DateTime.UtcNow
        };
    }

    public void Clear(string ident)
    {
        if (string.IsNullOrEmpty(ident))
            return;

        var prefix = ident + ":";
        var keysToRemove = _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    public void ClearAll()
    {
        _cache.Clear();
    }

    public int Prune(TimeSpan maxAge)
    {
        var threshold = DateTime.UtcNow - maxAge;
        var keysToRemove = _cache
            .Where(kvp => kvp.Value.LastAccessedUtc < threshold)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }

        return keysToRemove.Count;
    }
    
    public int Count => _cache.Count;

    private static string MakeKey(string ident, string dataHash) => $"{ident}:{dataHash}";

    private sealed class CacheEntry
    {
        public PairPerformanceMetrics Metrics { get; init; }
        public DateTime CreatedUtc { get; init; }
        public DateTime LastAccessedUtc { get; set; }
    }
}

public readonly record struct PairPerformanceMetrics(
    long TriangleCount,
    long ApproximateVramBytes);
