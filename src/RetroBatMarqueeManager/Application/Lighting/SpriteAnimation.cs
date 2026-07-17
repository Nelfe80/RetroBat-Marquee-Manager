using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Lighting;

/// <summary>
/// A decoded animated GIF (all frames composited), shared between instances.
/// Sprites are overlays composited after the main shader (CDC §23 / §17.5).
/// </summary>
public sealed class SpriteAnimation : IDisposable
{
    public required SKBitmap[] Frames { get; init; }
    public required int[] CumulativeMs { get; init; }
    public int TotalMs => CumulativeMs.Length > 0 ? CumulativeMs[^1] : 0;
    public bool Opaque { get; init; }

    private static readonly Dictionary<string, SpriteAnimation?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static SpriteAnimation? Load(string path, ILogger logger)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var cached)) return cached;
        }
        SpriteAnimation? animation = null;
        try
        {
            if (File.Exists(path))
            {
                using var codec = SKCodec.Create(path);
                if (codec != null && codec.FrameCount > 0)
                {
                    var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    var frameInfo = codec.FrameInfo;
                    var frames = new SKBitmap[codec.FrameCount];
                    var cumulative = new int[codec.FrameCount];
                    var elapsed = 0;

                    // blit budget: frames are pre-downscaled once at load so many
                    // sprites can fly at the same time on the CPU raster backend
                    const int maxHeight = 96;
                    var scaled = info.Height > maxHeight
                        ? new SKImageInfo(Math.Max(1, info.Width * maxHeight / info.Height), maxHeight,
                            SKColorType.Bgra8888, SKAlphaType.Premul)
                        : info;

                    for (var i = 0; i < codec.FrameCount; i++)
                    {
                        var bitmap = new SKBitmap(info);
                        // GIF frames may build on a required prior frame
                        var required = frameInfo[i].RequiredFrame;
                        if (required >= 0 && required < i)
                        {
                            using var priorFull = new SKBitmap(info);
                            // prior frames are stored downscaled: decode chains still
                            // need the full-res prior, so re-decode it
                            codec.GetPixels(info, priorFull.GetPixels(), new SKCodecOptions(required));
                            priorFull.GetPixelSpan().CopyTo(bitmap.GetPixelSpan());
                        }
                        codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(i, required));
                        if (scaled.Width != info.Width)
                        {
                            var small = bitmap.Resize(scaled, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                            bitmap.Dispose();
                            frames[i] = small ?? new SKBitmap(scaled);
                        }
                        else frames[i] = bitmap;
                        elapsed += Math.Max(20, frameInfo[i].Duration);
                        cumulative[i] = elapsed;
                    }
                    animation = new SpriteAnimation
                    {
                        Frames = frames,
                        CumulativeMs = cumulative,
                        Opaque = codec.Info.AlphaType == SKAlphaType.Opaque
                    };
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cannot decode sprite animation {Path}", path);
        }
        lock (Cache) { Cache[path] = animation; }
        return animation;
    }

    public SKBitmap FrameAt(double elapsedMs)
    {
        var wrapped = TotalMs > 0 ? elapsedMs % TotalMs : 0;
        for (var i = 0; i < CumulativeMs.Length; i++)
            if (wrapped < CumulativeMs[i]) return Frames[i];
        return Frames[^1];
    }

    public void Dispose()
    {
        foreach (var frame in Frames) frame.Dispose();
    }
}

/// <summary>
/// A live sprite on the marquee: random start, optional crossing trajectory with a
/// glowing trail, plays then fades out. Positions are normalized in the art rect.
/// </summary>
public sealed class SpriteInstance
{
    public required SpriteAnimation Animation { get; init; }
    public required float X { get; init; }
    public required float Y { get; init; }
    public required double StartSeconds { get; init; }
    public required double DurationSeconds { get; init; }
    /// <summary>Velocity in normalized units per second (0,0 = static pop).</summary>
    public float VelocityX { get; init; }
    public float VelocityY { get; init; }
    /// <summary>Trail glow color; null = no trail.</summary>
    public SkiaSharp.SKColor? TrailColor { get; init; }
    /// <summary>Per-instance size factor (adds variety in multi-spawns).</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>Grow effect: the sprite swells from Scale to Scale×2 over its life.</summary>
    public bool Grow { get; init; }

    /// <summary>Pixel-art sprites scaled up keep their crisp pixels (nearest neighbor).</summary>
    public bool PixelCrisp { get; init; }

    public float ScaleAt(double now)
    {
        if (!Grow) return Scale;
        var progress = Math.Clamp((now - StartSeconds) / DurationSeconds, 0, 1);
        return Scale * (1f + (float)progress);
    }

    public bool Done(double now) => now - StartSeconds >= DurationSeconds;

    public (float X, float Y) PositionAt(double now)
    {
        var t = (float)Math.Max(0, now - StartSeconds);
        return (X + VelocityX * t, Y + VelocityY * t);
    }

    /// <summary>Opacity envelope: quick in, hold, fade out on the last third.</summary>
    public float Alpha(double now)
    {
        var progress = (now - StartSeconds) / DurationSeconds;
        if (progress < 0.12) return (float)(progress / 0.12);
        if (progress > 0.66) return (float)Math.Max(0, 1 - (progress - 0.66) / 0.34);
        return 1f;
    }
}
