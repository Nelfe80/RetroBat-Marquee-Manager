using RetroBatMarqueeManager.Infrastructure.Rendering.Skia;
using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Lighting;

/// <summary>
/// Animated ingame events (docs\DECOUPLAGE-MOTEUR-EVENEMENTS.md): sprites and
/// color veils fired by the semantic .mem actions of ws/ingame. Split out of
/// <see cref="MarqueeLightingRenderer"/>, whose job is now only to LIGHT an image
/// — this one only PLAYS events, over whatever the surface shows (lit scene,
/// fanart, video, instruction card). It is therefore always transparent and is
/// mounted ABOVE the media stack, where the historical lighting layer sat below
/// it and let a fullscreen fanart bury every sprite.
///
/// Tube-level kinds degrade to their overlay component here (a strobe is a black
/// flicker, a blackout is a dark veil); when a lighting engine shares the surface
/// the controller ALSO routes those kinds to it, so the tubes still react
/// (§4a of the design note). `shake` belongs to neither renderer: the window
/// jolts its whole visual tree (§4b).
/// </summary>
public sealed class IngameEffectsRenderer : ISkiaFrameRenderer
{
    private const double FlickerHz = 24;

    private readonly ILogger _logger;
    private readonly SKPaint _glowPaint = new() { BlendMode = SKBlendMode.Plus };
    private readonly SKPaint _veilPaint = new();

    // effect state (veils / blackout), written under _fxLock
    private readonly object _fxLock = new();
    private IngameEffectRule? _activeFx;
    private double _fxStart = -1;
    private double _blackoutStart = -1;
    private double _blackoutUntil = -1;

    private readonly List<IngameEffectRule> _pendingSprites = new();
    private readonly List<SpriteInstance> _sprites = new();
    private readonly Random _fxRandom = new();

    private volatile bool _dirty = true;
    private long _steadySlot = -1;

    // rolling render cost (ms): drives the adaptive sprite budget
    private double _renderMsAverage = 8;

    public IngameEffectsRenderer(ILogger logger) => _logger = logger;

    /// <summary>
    /// 30 while an event plays — sprites want a smoother cadence than a tube-only
    /// scene. IDLE, the layer drops to 4: waking 30 times a second to decide there is
    /// nothing to draw costs a spin-wait per wake on a thread that runs at the
    /// process priority, and that competes with EmulationStation for nothing.
    /// </summary>
    public int DesiredFps => HasContent ? 30 : 4;

    public int ActiveSpriteCount => _sprites.Count;

    /// <summary>True while something is actually drawn. Drives both the cadence and
    /// the layer's visibility: an always-visible full-screen alpha layer is
    /// recomposited by WPF on every frame of every layer below it.</summary>
    public bool HasContent
    {
        get
        {
            if (_sprites.Count > 0 || _blackoutUntil > 0) return true;
            lock (_fxLock) { return _activeFx != null || _pendingSprites.Count > 0; }
        }
    }

    /// <summary>Semantic ingame event resolved by the effects library (ws/ingame).
    /// A rule can carry both a veil AND sprites — both fire.</summary>
    public void TriggerIngameEffect(IngameEffectRule rule)
    {
        lock (_fxLock)
        {
            if (rule.Sprite != null) _pendingSprites.Add(rule);
            if (rule.Kind != IngameEffectKind.Sprite)
            {
                _activeFx = rule;
                _fxStart = double.MinValue; // armed: stamped with the clock on the next frame
            }
        }
        _dirty = true;
    }

    /// <summary>Play session boundary (game start / return to the frontend): nothing
    /// of the previous session survives into the next one.</summary>
    public void SetIngame(bool ingame)
    {
        ResetSession();
        _dirty = true;
    }

    /// <summary>Drops running and pending effects, sprites included.</summary>
    public void ResetSession()
    {
        _blackoutStart = -1;
        _blackoutUntil = -1;
        _sprites.Clear();
        lock (_fxLock) { _activeFx = null; _pendingSprites.Clear(); }
    }

    /// <summary>Frame skip: the layer is idle — and costs nothing — as long as no
    /// event is running.</summary>
    public bool WantsFrame(TimeSpan elapsed)
    {
        if (_dirty) return true;
        if (_sprites.Count > 0 || _blackoutUntil > 0)
            return (long)(elapsed.TotalSeconds * FlickerHz) != _steadySlot;
        lock (_fxLock) { return _activeFx != null || _pendingSprites.Count > 0; }
    }

    /// <summary>Raised on the render thread when the layer goes from drawing
    /// something to drawing nothing, or back. The window mounts/unmounts the layer on
    /// it, so an idle events engine costs WPF exactly nothing.</summary>
    public event Action<bool>? ContentChanged;
    private bool _hadContent;

    /// <summary>
    /// FPS guard: how many sprites the current frame budget can afford. Fewer when
    /// the raster is already slow — but NEVER zero. Cutting sprites to 0 on a heavy
    /// surface made ingame effects vanish entirely ("one ring then nothing").
    /// </summary>
    private int SpriteBudget => _renderMsAverage switch
    {
        < 23 => 20,
        < 32 => 12,
        < 44 => 6,
        _ => 3
    };

    public void Render(SKCanvas canvas, int width, int height, TimeSpan elapsed)
    {
        var renderStart = System.Diagnostics.Stopwatch.GetTimestamp();

        var t = elapsed.TotalSeconds;
        _steadySlot = (long)(t * FlickerHz);
        _dirty = false;

        // always transparent: the media below IS the content, we only add to it
        canvas.Clear(SKColors.Transparent);

        var veil = UpdateIngameEffect(t);
        // the blackout darkens the BACKGROUND: its own sprites (game-over smoke…)
        // must still read over it, so it is drawn first
        DrawBlackout(canvas, width, height, t);
        SpawnSprites(t);
        DrawSprites(canvas, t, width, height);
        if (veil != null) DrawVeil(canvas, width, height, veil.Value);

        var renderMs = (System.Diagnostics.Stopwatch.GetTimestamp() - renderStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _renderMsAverage += (renderMs - _renderMsAverage) * 0.15;

        // this frame is the LAST one of the effect when it emptied the layer: it has
        // just been cleared to transparent, so the window can unmount it right after
        var content = HasContent;
        if (content != _hadContent)
        {
            _hadContent = content;
            ContentChanged?.Invoke(content);
        }
    }

    /// <summary>What a running effect asks the overlay to paint this frame.</summary>
    private readonly record struct Veil(SKColor Color, float Alpha, SKBlendMode Blend);

    /// <summary>Advances the running effect and returns the veil to paint, if any.
    /// Kinds owned elsewhere return null: `shake` (window transform), `powerCycle`
    /// (lighting engine). `blackout` arms its own window and returns null.</summary>
    private Veil? UpdateIngameEffect(double t)
    {
        IngameEffectRule? rule;
        double start;
        lock (_fxLock)
        {
            if (_activeFx == null) return null;
            if (_fxStart == double.MinValue) _fxStart = t;
            rule = _activeFx;
            start = _fxStart;
        }

        if (rule.Kind is IngameEffectKind.PowerCycle or IngameEffectKind.Blackout)
        {
            lock (_fxLock) { _activeFx = null; }
            if (rule.Kind == IngameEffectKind.Blackout)
            {
                _blackoutStart = t;
                _blackoutUntil = t + rule.DurationMs / 1000.0;
            }
            return null;
        }

        var progress = (t - start) / (rule.DurationMs / 1000.0);
        if (progress >= 1)
        {
            lock (_fxLock) { _activeFx = null; }
            return null;
        }
        var envelope = (float)Math.Sin(Math.PI * Math.Clamp(progress, 0, 1));

        switch (rule.Kind)
        {
            case IngameEffectKind.Shake:
                return null; // the window jolts its whole visual tree

            case IngameEffectKind.Strobe:
                // the tubes cutting out, seen from the overlay: black bursts at 18 Hz
                if ((long)(t * 18) % 2 == 0) return null;
                var depth = Math.Min(0.9f, rule.Dip > 0 ? rule.Dip : 0.75f);
                return new Veil(SKColors.Black, depth * envelope, SKBlendMode.SrcOver);

            case IngameEffectKind.Pulse:
                return new Veil(rule.Color, 0.28f * envelope, SKBlendMode.Plus);

            case IngameEffectKind.Tint:
                // sustained soft color grade
                return new Veil(rule.Color, 0.38f * envelope * 0.6f, SKBlendMode.SrcOver);

            default:
                return new Veil(rule.Color, 0.38f * envelope, SKBlendMode.SrcOver);
        }
    }

    /// <summary>Power cut: a deep veil that fades in and back out over the window.
    /// Not pure black — the surface must read as "gone dark", not as a hole.</summary>
    private void DrawBlackout(SKCanvas canvas, int width, int height, double t)
    {
        if (_blackoutUntil <= 0) return;
        if (t >= _blackoutUntil)
        {
            _blackoutStart = -1;
            _blackoutUntil = -1;
            return;
        }

        var span = _blackoutUntil - _blackoutStart;
        var progress = span > 0 ? (t - _blackoutStart) / span : 1;
        var envelope = progress < 0.08 ? progress / 0.08
            : progress > 0.75 ? Math.Max(0, (1 - progress) / 0.25)
            : 1.0;
        DrawVeil(canvas, width, height, new Veil(SKColors.Black, (float)(0.88 * envelope), SKBlendMode.SrcOver));
    }

    private void DrawVeil(SKCanvas canvas, int width, int height, Veil veil)
    {
        if (veil.Alpha <= 0.002f) return;
        _veilPaint.BlendMode = veil.Blend;
        _veilPaint.Color = veil.Color.WithAlpha((byte)(Math.Clamp(veil.Alpha, 0f, 1f) * 255));
        canvas.DrawRect(0, 0, width, height, _veilPaint);
        _veilPaint.Color = SKColors.White;
        _veilPaint.BlendMode = SKBlendMode.SrcOver;
    }

    /// <summary>Spawn pending sprite rules across the surface.</summary>
    private void SpawnSprites(double t)
    {
        List<IngameEffectRule>? pending = null;
        lock (_fxLock)
        {
            if (_pendingSprites.Count > 0)
            {
                pending = new List<IngameEffectRule>(_pendingSprites);
                _pendingSprites.Clear();
            }
        }
        if (pending == null) return;
        foreach (var rule in pending)
        {
            if (rule.Sprite == null) continue;
            var animation = SpriteAnimation.Load(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "sprites", rule.Sprite), _logger);
            if (animation == null) continue;
            // "full_" sprites are scene backdrops: exactly ONE in the scene at a
            // time, spanning 100 % of the surface width
            var fullWidth = Path.GetFileName(rule.Sprite).StartsWith("full_", StringComparison.OrdinalIgnoreCase);
            if (fullWidth) _sprites.RemoveAll(s => s.FullWidth);

            var budget = SpriteBudget;
            var count = fullWidth ? 1 : rule.Count;
            for (var i = 0; i < count && _sprites.Count < budget; i++)
            {
                var duration = Math.Max(0.3, rule.DurationMs / 1000.0) * (0.85 + _fxRandom.NextDouble() * 0.3);

                // placement of the SPAWN point: random draws are STRATIFIED (one
                // horizontal band per sprite: halves, quarters, tenths… so a
                // swarm never clumps), or centered, or evenly spread
                float px, py;
                switch (rule.Placement)
                {
                    case "center":
                        px = 0.5f;
                        py = 0.5f;
                        break;
                    case "spread":
                        px = (i + 0.5f) / count;
                        py = 0.45f;
                        break;
                    default:
                        px = (i + 0.15f + (float)_fxRandom.NextDouble() * 0.7f) / count;
                        py = 0.18f + (float)_fxRandom.NextDouble() * 0.58f;
                        break;
                }
                if (fullWidth)
                {
                    px = 0.5f;
                    py = 0.5f;
                }

                float x = px, y = py, vx = 0, vy = 0;
                switch (rule.Motion)
                {
                    case "cross":
                        // horizontal crossing: random side, placement height, slight slope
                        var leftToRight = _fxRandom.NextDouble() < 0.5;
                        x = leftToRight ? -0.08f : 1.08f;
                        y = rule.Placement == "random" ? 0.18f + (float)_fxRandom.NextDouble() * 0.6f : py;
                        vx = (float)((1.16 / duration) * (leftToRight ? 1 : -1));
                        vy = (float)((_fxRandom.NextDouble() - 0.5) * 0.25);
                        break;
                    case "fall":
                        // short vertical drop: exits fast, freeing a slot for the next one
                        y = -0.15f;
                        vy = (float)(1.35 / duration);
                        vx = rule.Placement == "random" ? (float)((_fxRandom.NextDouble() - 0.5) * 0.10) : 0;
                        break;
                    case "rise":
                        y = 1.15f;
                        vy = (float)(-1.35 / duration);
                        vx = rule.Placement == "random" ? (float)((_fxRandom.NextDouble() - 0.5) * 0.10) : 0;
                        break;
                }

                // the historic size jitter only applies in random placement — a
                // deliberate scale (200 %…) must render exactly as asked
                var jitter = !fullWidth && rule.Placement == "random" && Math.Abs(rule.Scale - 1.0) < 0.01
                    ? 0.8f + (float)_fxRandom.NextDouble() * 0.5f
                    : 1f;
                _sprites.Add(new SpriteInstance
                {
                    Animation = animation,
                    X = x,
                    Y = y,
                    VelocityX = fullWidth ? 0 : vx,
                    VelocityY = fullWidth ? 0 : vy,
                    TrailColor = rule.TrailColor,
                    Scale = (float)rule.Scale * jitter,
                    Grow = !fullWidth && rule.Grow,
                    PixelCrisp = rule.Scale >= 1.5,
                    FullWidth = fullWidth,
                    StartSeconds = t + i * (0.06 + _fxRandom.NextDouble() * 0.08),
                    DurationSeconds = duration
                });
            }
        }
    }

    /// <summary>Animated sprite overlays (CDC §23): glowing trail ghosts behind
    /// moving sprites, then the animated frame.</summary>
    private void DrawSprites(SKCanvas canvas, double t, float w, float h)
    {
        if (_sprites.Count == 0) return;
        // a sprite out of the field frees its slot immediately
        _sprites.RemoveAll(sprite =>
        {
            if (sprite.Done(t)) return true;
            var (px, py) = sprite.PositionAt(t);
            return px < -0.2f || px > 1.2f || py < -0.25f || py > 1.25f;
        });
        foreach (var sprite in _sprites)
        {
            if (t < sprite.StartSeconds) continue;
            var alpha = sprite.Alpha(t);
            var (nx, ny) = sprite.PositionAt(t);

            // optional light trail (2 ghosts max — trails are expensive on CPU raster)
            if (sprite.TrailColor is { } trail && (sprite.VelocityX != 0 || sprite.VelocityY != 0))
            {
                for (var k = 2; k >= 1; k--)
                {
                    var (gx, gy) = sprite.PositionAt(t - k * 0.06);
                    var ghostAlpha = alpha * (1f - k / 3f) * 0.4f;
                    DrawStretchedGlow(canvas, new SKPoint(gx * w, gy * h),
                        0.08f * h * sprite.Scale, 1.4f, 0.8f,
                        trail.WithAlpha((byte)(ghostAlpha * 255)));
                }
            }

            var frame = sprite.Animation.FrameAt((t - sprite.StartSeconds) * 1000);
            float width, height;
            if (sprite.FullWidth)
            {
                // backdrop sprite: spans the whole surface width
                width = w;
                height = width * frame.Height / frame.Width;
            }
            else
            {
                height = 0.30f * h * sprite.ScaleAt(t);
                width = height * frame.Width / frame.Height;
            }
            var dest = SKRect.Create(nx * w - width / 2f, ny * h - height / 2f, width, height);
            _glowPaint.BlendMode = sprite.Animation.Opaque ? SKBlendMode.Screen : SKBlendMode.SrcOver;
            _glowPaint.Color = SKColors.White.WithAlpha((byte)(alpha * 255));
            if (sprite.PixelCrisp)
            {
                // deliberate upscales keep the pixel-art look
                using var image = SKImage.FromBitmap(frame);
                canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Nearest), _glowPaint);
            }
            else
            {
                canvas.DrawBitmap(frame, dest, _glowPaint);
            }
        }
        _glowPaint.Color = SKColors.White;
        _glowPaint.BlendMode = SKBlendMode.Plus;
    }

    private void DrawStretchedGlow(SKCanvas canvas, SKPoint center, float radius, float scaleX, float scaleY, SKColor color)
    {
        canvas.Save();
        canvas.Translate(center.X, center.Y);
        canvas.Scale(scaleX, scaleY);
        using var gradient = SKShader.CreateRadialGradient(new SKPoint(0, 0), radius,
            new[] { color, color.WithAlpha(0) }, null, SKShaderTileMode.Clamp);
        _glowPaint.Shader = gradient;
        canvas.DrawCircle(0, 0, radius, _glowPaint);
        _glowPaint.Shader = null;
        canvas.Restore();
    }

    public void Dispose()
    {
        ResetSession();
        _glowPaint.Dispose();
        _veilPaint.Dispose();
    }
}
