using System.Diagnostics;

namespace RetroBatMarqueeManager.Infrastructure.Rendering.Skia;

/// <summary>
/// Lot 0 instrumentation (docs\Update.txt §4): measures the render/present pipeline
/// WITHOUT changing its behaviour. Cheap on the hot path — counters are lock-free,
/// timing samples land in small single-writer ring buffers, percentiles are computed
/// only when a snapshot is taken (every few seconds). Two writer threads are involved
/// (render thread for render times, UI thread for present times); each buffer has a
/// single writer so no cross-thread contention on the hot path.
/// </summary>
public sealed class RenderMetrics
{
    private readonly Samples _renderMs = new();
    private readonly Samples _writePixelsMs = new();
    private readonly Samples _swapLockWaitMs = new();

    private long _renderedFrames;
    private long _presentedFrames;
    private long _skippedFrames;
    private long _droppedPresents;

    private volatile int _activeSpriteCount;
    private double _lastMapGenerationMs;

    private long _lastAllocatedBytes = GC.GetTotalAllocatedBytes(false);
    private int _lastGc0 = GC.CollectionCount(0);
    private int _lastGc1 = GC.CollectionCount(1);
    private int _lastGc2 = GC.CollectionCount(2);
    private long _lastSnapshotTicks = Stopwatch.GetTimestamp();

    public void RecordRender(double ms) { Interlocked.Increment(ref _renderedFrames); _renderMs.Add(ms); }
    public void RecordSkip() => Interlocked.Increment(ref _skippedFrames);
    public void RecordPresent(double swapLockWaitMs, double writePixelsMs)
    {
        Interlocked.Increment(ref _presentedFrames);
        _swapLockWaitMs.Add(swapLockWaitMs);
        _writePixelsMs.Add(writePixelsMs);
    }
    public void RecordDroppedPresent() => Interlocked.Increment(ref _droppedPresents);
    public void RecordMapGeneration(double ms) => Volatile.Write(ref _lastMapGenerationMs, ms);
    public void RecordSpriteCount(int count) => _activeSpriteCount = count;

    /// <summary>Formatted one-line snapshot over the elapsed window; resets counters.</summary>
    public string SnapshotLine(double adaptiveScale, int width, int height)
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = Math.Max(0.001, (now - _lastSnapshotTicks) / (double)Stopwatch.Frequency);
        _lastSnapshotTicks = now;

        var rendered = Interlocked.Exchange(ref _renderedFrames, 0);
        var presented = Interlocked.Exchange(ref _presentedFrames, 0);
        var skipped = Interlocked.Exchange(ref _skippedFrames, 0);
        var dropped = Interlocked.Exchange(ref _droppedPresents, 0);

        var (rP50, rP95, rP99) = _renderMs.Percentiles();
        var wP95 = _writePixelsMs.Percentiles().P95;
        var sP95 = _swapLockWaitMs.Percentiles().P95;

        var allocatedNow = GC.GetTotalAllocatedBytes(false);
        var allocPerSec = (allocatedNow - _lastAllocatedBytes) / seconds;
        _lastAllocatedBytes = allocatedNow;
        var gc0 = GC.CollectionCount(0); var gc1 = GC.CollectionCount(1); var gc2 = GC.CollectionCount(2);
        var dGc0 = gc0 - _lastGc0; var dGc1 = gc1 - _lastGc1; var dGc2 = gc2 - _lastGc2;
        _lastGc0 = gc0; _lastGc1 = gc1; _lastGc2 = gc2;

        return $"[RenderMetrics] {width}x{height} scale={adaptiveScale:P0} | "
             + $"renderedFps={rendered / seconds:F1} presentedFps={presented / seconds:F1} "
             + $"skipped={skipped} droppedPresents={dropped} | "
             + $"renderMs p50={rP50:F1} p95={rP95:F1} p99={rP99:F1} | "
             + $"writePixelsMs p95={wP95:F1} swapLockWaitMs p95={sP95:F1} | "
             + $"mapGenMs={Volatile.Read(ref _lastMapGenerationMs):F0} sprites={_activeSpriteCount} | "
             + $"alloc={allocPerSec / (1024 * 1024):F1}MiB/s gc={dGc0}/{dGc1}/{dGc2}";
    }

    /// <summary>Fixed-capacity single-writer sample buffer; percentiles on demand.</summary>
    private sealed class Samples
    {
        private const int Capacity = 1024;
        private readonly double[] _values = new double[Capacity];
        private readonly object _sync = new();
        private int _count;
        private int _head;

        public void Add(double value)
        {
            lock (_sync)
            {
                _values[_head] = value;
                _head = (_head + 1) % Capacity;
                if (_count < Capacity) _count++;
            }
        }

        public (double P50, double P95, double P99) Percentiles()
        {
            double[] copy;
            int count;
            lock (_sync)
            {
                count = _count;
                if (count == 0) return (0, 0, 0);
                copy = new double[count];
                Array.Copy(_values, copy, count);
                _count = 0;
                _head = 0;
            }
            Array.Sort(copy);
            return (At(copy, 0.50), At(copy, 0.95), At(copy, 0.99));
        }

        private static double At(double[] sorted, double q)
        {
            var index = (int)Math.Clamp(Math.Ceiling(q * sorted.Length) - 1, 0, sorted.Length - 1);
            return sorted[index];
        }
    }
}
