using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Lighting;

/// <summary>
/// Lot C: LRU cache of generated lighting scenes, keyed by image + mtime + surface
/// size + profile. Revisiting a game (or a size already produced) becomes a cache hit
/// that skips the image decode, framing and per-pixel map generation entirely.
///
/// Ownership is deliberately share-free: the cache stores its OWN deep copies of the
/// maps (<see cref="LightingMaps.Clone"/>) and hands the caller a FRESH clone on every
/// hit. Nothing outside the cache ever references a cache-owned bitmap, so eviction can
/// dispose freely and can never free a map the renderer is displaying.
/// </summary>
public sealed class LightingMapCache : IDisposable
{
    public sealed record CachedScene(
        LightingMaps Maps, SKPoint Offset, int SurfaceWidth, int SurfaceHeight,
        ResolvedLightProfile Profile, BacklightProfile Backlight, RbMarqueeScene? LampScene, string? Rom);

    private readonly long _maxBytes;
    private readonly object _lock = new();
    private readonly LinkedList<(string Key, CachedScene Scene, long Bytes)> _lru = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, CachedScene Scene, long Bytes)>> _index = new(StringComparer.Ordinal);
    private long _bytes;

    public LightingMapCache(long maxBytes) => _maxBytes = Math.Max(8L * 1024 * 1024, maxBytes);

    /// <summary>On a hit, returns a scene whose <c>Maps</c> is a FRESH clone the caller
    /// owns (and disposes like any generated map); the cache copy stays pristine. The
    /// clone is done under the lock so a concurrent eviction can never race it.</summary>
    public bool TryGetClone(string key, out CachedScene? scene)
    {
        lock (_lock)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node); // most-recently-used
                var copy = node.Value.Scene.Maps.Clone();
                if (copy != null)
                {
                    scene = node.Value.Scene with { Maps = copy };
                    return true;
                }
            }
        }
        scene = null;
        return false;
    }

    /// <summary>Stores a DEEP COPY of <paramref name="maps"/>; the caller keeps ownership
    /// of the original. No-op if the copy fails or the key is already cached.</summary>
    public void Put(string key, LightingMaps maps, SKPoint offset, int surfaceWidth, int surfaceHeight,
        ResolvedLightProfile profile, BacklightProfile backlight, RbMarqueeScene? lampScene, string? rom)
    {
        var copy = maps.Clone();
        if (copy == null) return;
        var bytes = copy.ByteSize;
        lock (_lock)
        {
            if (_index.ContainsKey(key)) { copy.Dispose(); return; }
            var scene = new CachedScene(copy, offset, surfaceWidth, surfaceHeight, profile, backlight, lampScene, rom);
            var node = new LinkedListNode<(string Key, CachedScene Scene, long Bytes)>((key, scene, bytes));
            _lru.AddFirst(node);
            _index[key] = node;
            _bytes += bytes;
            EvictToBudget();
        }
    }

    private void EvictToBudget()
    {
        while (_bytes > _maxBytes && _lru.Last is { } last)
        {
            _lru.RemoveLast();
            _index.Remove(last.Value.Key);
            _bytes -= last.Value.Bytes;
            last.Value.Scene.Maps.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var entry in _lru) entry.Scene.Maps.Dispose();
            _lru.Clear();
            _index.Clear();
            _bytes = 0;
        }
    }
}
