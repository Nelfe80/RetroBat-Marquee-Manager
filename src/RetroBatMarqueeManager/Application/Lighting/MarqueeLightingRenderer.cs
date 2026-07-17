using System.Runtime.InteropServices;
using RetroBatMarqueeManager.Infrastructure.Rendering.Skia;
using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Lighting;

/// <summary>
/// Lighting renderer: drive-lerp composition (final = unlit×(1−drive) + lit×drive,
/// faithful to the source at full drive) with per-tube life simulation — randomized
/// ignition, flicker, dips, brown-outs, restrikes, occasional death. The physical
/// profile (bulb, aging) is resolved from the game metadata via the XML libraries
/// (§15); framing is content-aware (never cut the title). The vertical tube glow is
/// a 1×H lookup rebuilt each rendered frame. The layer renders fully transparent
/// until maps exist, so the static image below stays the fallback (§4.5).
/// </summary>
public sealed class MarqueeLightingRenderer : ISkiaFrameRenderer
{
    private const string Sksl = @"
uniform shader unlitTex;
uniform shader litTex;
uniform shader shapeTex;   // 1xH vertical tube glow lookup, updated per frame
uniform float2 offset;
uniform float  k;          // ShapeScale: restores the lookup's 0..1.2 physical range

half4 main(float2 p) {
    float2 q = p - offset;
    half3 unlit = unlitTex.eval(q).rgb;
    half3 lit   = litTex.eval(q).rgb;
    float drive = float(shapeTex.eval(float2(0.5, q.y)).r) * k;

    float3 color = float3(unlit) * max(1.0 - drive, 0.0) + float3(lit) * drive;
    return half4(half3(clamp(color, 0.0, 1.0)), 1.0);
}";

    /// <summary>Physical shape range [0..1.2] stored normalized in the 8-bit lookup.</summary>
    private const float ShapeScale = 1.2f;
    private const double FlickerHz = 24;

    private sealed record MarqueeRequest(string Path, LightingSceneMeta? Meta);

    private readonly ILogger _logger;
    private readonly double _fillHeightMaxCrop;
    private readonly float _glassReflection;
    private readonly float _tubeVisualOpacity;
    private readonly LightingLibraries _libraries;
    private readonly Infrastructure.Audio.LightingSoundService? _sound;
    private readonly SKRuntimeEffect _effect;
    private readonly SKPaint _paint = new();
    private readonly SKPaint _glowPaint = new() { BlendMode = SKBlendMode.Plus };
    private readonly SKPaint _glassPaint = new();
    private readonly SKBitmap? _tubeVisual;

    // requested by the UI thread, consumed by the render thread
    private volatile MarqueeRequest? _requested;
    private volatile bool _dirty = true;
    private volatile bool _powerCycleRequested;
    private volatile bool _ingame;

    // per-game lamp scene (rbmarquee.xml) + live arcade outputs
    private RbMarqueeScene? _lampScene;
    private LampState[] _lampStates = Array.Empty<LampState>();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _arcadeOutputs = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _arcadeLive;

    // ingame effect state (flash / pulse / blackout), written under _fxLock
    private readonly object _fxLock = new();
    private IngameEffectRule? _activeFx;
    private double _fxStart = -1;
    private double _blackoutUntil = -1;

    // sprite overlays (coins…): spawned by ingame events, drawn after the shader
    private readonly List<SpriteInstance> _sprites = new();

    // audio-driven flicker sequence: the sound's envelope drives one tube
    private Infrastructure.Audio.LightingSoundService.SequenceHandle? _sequence;
    private int _sequenceTubeIndex;
    private double _sequenceStart;
    private double _nextSequenceAt = double.MaxValue;
    private float _lastSequenceLevel;
    private readonly Random _fxRandom = new();

    // render-thread state
    private string? _currentPath;
    private LightingMaps? _maps;
    private SKShader? _unlitShader, _litShader;
    private SKPoint _offset;
    private int _surfaceWidth, _surfaceHeight;
    private double _sceneReadyAt = -1;
    private long _steadySlot = -1;
    private ResolvedLightProfile _profile = null!;
    private BacklightProfile _backlightProfile = BacklightProfile.Resolve(false, 4.0);
    private float[] _tubeRow1 = Array.Empty<float>();
    private float[]? _tubeRow2;
    private TubeLifeSimulator[] _tubes = Array.Empty<TubeLifeSimulator>();
    private SKBitmap? _shapeRow;
    private byte[]? _shapeBuffer;

    // background generation handoff
    private readonly object _resultLock = new();
    private (string Path, LightingMaps Maps, SKPoint Offset, int W, int H, ResolvedLightProfile Profile, BacklightProfile Backlight, RbMarqueeScene? LampScene)? _pendingResult;
    private volatile bool _generating;

    public MarqueeLightingRenderer(ILogger logger, LightingLibraries libraries, double fillHeightMaxCrop = 0.30,
        Infrastructure.Audio.LightingSoundService? sound = null, double glassReflection = 0.06,
        string? tubeVisualPath = null, double tubeVisualOpacity = 0.0)
    {
        _logger = logger;
        _libraries = libraries;
        _sound = sound;
        _fillHeightMaxCrop = Math.Clamp(fillHeightMaxCrop, 0.0, 0.6);
        _glassReflection = (float)Math.Clamp(glassReflection, 0.0, 0.3);
        _tubeVisualOpacity = (float)Math.Clamp(tubeVisualOpacity, 0.0, 0.5);
        _effect = SKRuntimeEffect.CreateShader(Sksl, out var errors)
                  ?? throw new InvalidOperationException($"SKSL compilation failed: {errors}");
        if (_tubeVisualOpacity > 0 && tubeVisualPath != null && File.Exists(tubeVisualPath))
            _tubeVisual = SKBitmap.Decode(tubeVisualPath);
    }

    /// <summary>
    /// Full power cycle of the current scene: tubes go dark and re-ignite with new
    /// random scenarios (game launch / return-to-frontend drama). Maps are kept.
    /// </summary>
    public void PowerCycle()
    {
        _powerCycleRequested = true;
        _dirty = true;
    }

    /// <summary>Ingame = clean session: lighting sounds muted, attract mode paused.</summary>
    public void SetIngame(bool ingame)
    {
        _ingame = ingame;
        if (!ingame) { _arcadeOutputs.Clear(); _arcadeLive = false; }
        _dirty = true;
    }

    /// <summary>Live MAME output (ws/arcade): drives the mapped lamp of the scene.</summary>
    public void SetArcadeOutput(string output, int value)
    {
        _arcadeOutputs[output] = value;
        _arcadeLive = true;
        _dirty = true;
    }

    /// <summary>Semantic ingame event resolved by the effects library (ws/ingame).
    /// A rule can carry both a glass flash and sprites — both fire.</summary>
    public void TriggerIngameEffect(IngameEffectRule rule)
    {
        lock (_fxLock)
        {
            if (rule.Sprite != null) _pendingSprites.Add(rule);
            if (rule.Kind != IngameEffectKind.Sprite)
            {
                _activeFx = rule;
                _fxStart = double.MinValue; // armed: stamped with scene time on next frame
            }
        }
        _dirty = true;
    }

    private readonly List<IngameEffectRule> _pendingSprites = new();

    /// <summary>Spawn pending sprite rules at random positions in the art rect.</summary>
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
            // time, spanning 100 % of the artwork width
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

    /// <summary>Animated sprite overlays, composited after the shader (CDC §23):
    /// glowing trail ghosts behind moving sprites, then the animated frame.</summary>
    private void DrawSprites(SKCanvas canvas, double t, float w, float h, SKPoint offset)
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
                    DrawStretchedGlow(canvas,
                        new SKPoint(offset.X + gx * w, offset.Y + gy * h),
                        0.08f * h * sprite.Scale, 1.4f, 0.8f,
                        trail.WithAlpha((byte)(ghostAlpha * 255)));
                }
            }

            var frame = sprite.Animation.FrameAt((t - sprite.StartSeconds) * 1000);
            float width, height;
            if (sprite.FullWidth)
            {
                // backdrop sprite: spans the whole artwork width
                width = w;
                height = width * frame.Height / frame.Width;
            }
            else
            {
                height = 0.30f * h * sprite.ScaleAt(t);
                width = height * frame.Width / frame.Height;
            }
            var dest = SKRect.Create(
                offset.X + nx * w - width / 2f,
                offset.Y + ny * h - height / 2f, width, height);
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

    /// <summary>True when a lighting scene is mounted (used by the DMD mirror gate).</summary>
    public bool HasScene => _maps != null;

    /// <summary>Artwork rectangle in surface pixels — the DMD mirror crops to it
    /// so a small centered logo still fills the panel.</summary>
    public SKRectI ArtRect
    {
        get
        {
            var maps = _maps;
            if (maps == null) return SKRectI.Empty;
            return new SKRectI((int)_offset.X, (int)_offset.Y,
                (int)_offset.X + maps.Width, (int)_offset.Y + maps.Height);
        }
    }

    /// <summary>Thread-safe: called from the UI thread. Null clears the scene (video / .lay / stop).</summary>
    public void SetMarqueeImage(string? path, LightingSceneMeta? meta = null)
    {
        _requested = path == null ? null : new MarqueeRequest(path, meta);
        _dirty = true;
    }

    /// <summary>
    /// §17.5 frame skip: lively bulbs render at the flicker cadence (24 Hz), a
    /// steady LED scene never re-renders, everything else renders on demand.
    /// </summary>
    public bool WantsFrame(TimeSpan elapsed)
    {
        if (_dirty || _generating) return true;
        lock (_resultLock) { if (_pendingResult != null) return true; }
        if (_maps == null)
        {
            // sceneless (video marquee, console game): the ingame effects still
            // animate as an overlay at the flicker cadence
            if (_sprites.Count > 0) return (long)(elapsed.TotalSeconds * FlickerHz) != _steadySlot;
            lock (_fxLock) { return _activeFx != null || _pendingSprites.Count > 0; }
        }
        // anything alive keeps the flicker cadence: lamps (attract/outputs),
        // ingame effects, sprites, blackout, audio-driven sequence
        if (_lampStates.Length > 0 || _sequence != null || _blackoutUntil > 0 || _sprites.Count > 0)
            return (long)(elapsed.TotalSeconds * FlickerHz) != _steadySlot;
        lock (_fxLock) { if (_activeFx != null || _pendingSprites.Count > 0) return true; }
        // a bulb with no flicker and no life events settles into a static frame
        if (_profile.Bulb.EventRateScale <= 0 && _profile.Bulb.FlickerAmount <= 0)
            return elapsed.TotalSeconds - _sceneReadyAt < 0.4;
        return (long)(elapsed.TotalSeconds * FlickerHz) != _steadySlot;
    }

    // rolling render cost (ms): drives the adaptive sprite budget
    private double _renderMsAverage = 8;

    /// <summary>
    /// FPS guard: how many sprites the current frame budget can afford. Below
    /// ~55% of the 24 Hz budget everything is allowed; past the budget, spawning
    /// stops entirely until frames recover.
    /// </summary>
    private int SpriteBudget => _renderMsAverage switch
    {
        < 23 => 20,
        < 32 => 10,
        < 40 => 4,
        _ => 0
    };

    public void Render(SKCanvas canvas, int width, int height, TimeSpan elapsed)
    {
        var renderStart = System.Diagnostics.Stopwatch.GetTimestamp();
        RenderCore(canvas, width, height, elapsed);
        var renderMs = (System.Diagnostics.Stopwatch.GetTimestamp() - renderStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _renderMsAverage += (renderMs - _renderMsAverage) * 0.15;
    }

    private void RenderCore(SKCanvas canvas, int width, int height, TimeSpan elapsed)
    {
        AdoptPendingResult(elapsed);

        if (_powerCycleRequested)
        {
            _powerCycleRequested = false;
            if (_maps != null)
            {
                _sceneReadyAt = elapsed.TotalSeconds;
                CreateTubeSimulators();
            }
        }

        var requested = _requested;
        if (requested == null && _currentPath != null) ClearScene();
        var needsGeneration = requested != null &&
                              (requested.Path != _currentPath || _surfaceWidth != width || _surfaceHeight != height);
        if (needsGeneration && !_generating)
            StartGeneration(requested!, width, height);

        _steadySlot = (long)(elapsed.TotalSeconds * FlickerHz);
        _dirty = false;

        if (_maps == null || _currentPath == null)
        {
            // no scene: transparent so the media below stays visible — but the
            // ingame effects still draw. A video marquee (console games) unmounts
            // the lighting scene; sprites and veils must survive it.
            canvas.Clear(SKColors.Transparent);
            _sound?.SetLevels(0f, 0f);
            RenderOverlayFx(canvas, width, height, elapsed.TotalSeconds);
            return;
        }

        // scene active: opaque black letterbox around the marquee (CDC §20.3 background="black")
        canvas.Clear(SKColors.Black);

        // advance the life of each tube
        var t = elapsed.TotalSeconds - _sceneReadyAt;
        foreach (var tube in _tubes) tube.Update(t);
        var i1 = _tubes.Length > 0 ? (float)_tubes[0].Intensity : 1f;
        var i2 = _tubes.Length > 1 ? (float)_tubes[1].Intensity : 0f;

        // audio-driven flicker: the sequence's live amplitude replaces one tube's intensity
        UpdateAudioSequence(t, ref i1, ref i2);

        // ingame effect envelope (flash dip / blackout)
        var fx = UpdateIngameEffect(t, ref i1, ref i2);

        SyncSound(i1, i2);
        UpdateShapeRow(i1, i2);
        UpdateLamps(t);
        using var shapeShader = _shapeRow!.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
            new SKSamplingOptions(SKFilterMode.Linear));

        var uniforms = new SKRuntimeEffectUniforms(_effect)
        {
            ["offset"] = new[] { _offset.X, _offset.Y },
            ["k"] = ShapeScale
        };
        var children = new SKRuntimeEffectChildren(_effect)
        {
            ["unlitTex"] = _unlitShader,
            ["litTex"] = _litShader,
            ["shapeTex"] = shapeShader
        };
        var shaking = _shakeX != 0f || _shakeY != 0f;
        if (shaking) { canvas.Save(); canvas.Translate(_shakeX, _shakeY); }

        using var shader = _effect.ToShader(uniforms, children);
        _paint.Shader = shader;
        canvas.DrawRect(_offset.X, _offset.Y, _maps.Width, _maps.Height, _paint);
        _paint.Shader = null;

        DrawTubeVisual(canvas);
        if (_tubes.Length > 0 && _tubes[0].Electrode > 0.001)
            DrawElectrodeGlow(canvas, (float)_tubes[0].Electrode, _backlightProfile.TubeY1);
        if (_tubes.Length > 1 && _tubes[1].Electrode > 0.001)
            DrawElectrodeGlow(canvas, (float)_tubes[1].Electrode, _backlightProfile.TubeY2);
        DrawLamps(canvas);
        SpawnSprites(t);
        DrawSprites(canvas, t, _maps.Width, _maps.Height, _offset);
        if (shaking) canvas.Restore();
        if (fx != null) DrawIngameEffect(canvas, fx.Value.Rule, fx.Value.Envelope, _maps.Width, _maps.Height, _offset);
        DrawGlass(canvas);
    }

    /// <summary>Sceneless ingame effects: sprites and color veils drawn over the
    /// whole surface while no lighting scene is mounted (video marquee, console
    /// game without artwork). Tube-level effects (dip, strobe, shake, blackout)
    /// have no tubes to act on and reduce to their overlay component.</summary>
    private void RenderOverlayFx(SKCanvas canvas, int width, int height, double t)
    {
        float i1 = 1f, i2 = 1f;
        var fx = UpdateIngameEffect(t, ref i1, ref i2);
        SpawnSprites(t);
        DrawSprites(canvas, t, width, height, default);
        if (fx != null) DrawIngameEffect(canvas, fx.Value.Rule, fx.Value.Envelope, width, height, default);
    }

    /// <summary>
    /// While a long sound sequence plays, its live amplitude drives one tube: what
    /// you hear is what you see. Short ticks fire on sharp rises. Scheduled rarely,
    /// never while ingame; sounds never loop.
    /// </summary>
    private void UpdateAudioSequence(double t, ref float i1, ref float i2)
    {
        if (_sound == null || _profile.Bulb.SolidState) return;

        if (_sequence == null)
        {
            if (_ingame) return;
            if (_nextSequenceAt == double.MaxValue && t > 8)
                _nextSequenceAt = t + 15 + Exponential(45);
            if (t < _nextSequenceAt) return;

            var files = Infrastructure.Audio.LightingSoundService.SequenceFiles;
            _sequence = _sound.PlaySequence(files[_fxRandom.Next(files.Length)], 0.85f);
            _nextSequenceAt = t + 30 + Exponential(60);
            if (_sequence == null) return;
            _sequenceTubeIndex = _tubes.Length > 1 ? _fxRandom.Next(2) : 0;
            _sequenceStart = t;
            _lastSequenceLevel = 1f;
        }

        var maxWindow = Math.Min(_sequence.Duration, 12.0);
        if (_sequence.Done || t - _sequenceStart > maxWindow || _ingame)
        {
            _sound.StopSequence(_sequence);
            _sequence = null;
            return;
        }

        var level = _sequence.Level;
        var driven = 0.10f + level * 1.02f;
        if (_sequenceTubeIndex == 0 || _tubes.Length == 1) i1 = driven; else i2 = driven;

        // calibrated short tick on each sharp rise, in sync with the flicker
        if (level > 0.55f && _lastSequenceLevel <= 0.45f && !_ingame)
            _sound.PlayOneShot("ampoule-on-off.mp3", 0.45f);
        _lastSequenceLevel = level;
    }

    private double Exponential(double mean) => -mean * Math.Log(1 - _fxRandom.NextDouble());

    /// <summary>Ingame effect envelope: dips/cuts tube intensity; returns overlay draw data.</summary>
    private (IngameEffectRule Rule, float Envelope)? UpdateIngameEffect(double t, ref float i1, ref float i2)
    {
        // blackout window: everything dark, then re-ignite
        if (_blackoutUntil > 0)
        {
            if (t < _blackoutUntil) { i1 = i2 = 0.02f; return null; }
            _blackoutUntil = -1;
            if (_maps != null)
            {
                _sceneReadyAt += t; // restart scene clock: re-ignition
                CreateTubeSimulators();
            }
            return null;
        }

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
                _blackoutUntil = t + rule.DurationMs / 1000.0;
            else
                _powerCycleRequested = true;
            return null;
        }

        var progress = (t - start) / (rule.DurationMs / 1000.0);
        if (progress >= 1)
        {
            lock (_fxLock) { _activeFx = null; }
            _shakeX = _shakeY = 0f;
            return null;
        }
        var envelope = (float)Math.Sin(Math.PI * Math.Clamp(progress, 0, 1));

        switch (rule.Kind)
        {
            case IngameEffectKind.Shake:
                // physical jolt of the whole scene, decaying with the envelope
                var amplitude = (rule.Dip > 0 ? rule.Dip : 0.5f) * envelope;
                _shakeX = (float)((_fxRandom.NextDouble() - 0.5) * 2 * amplitude * 14);
                _shakeY = (float)((_fxRandom.NextDouble() - 0.5) * 2 * amplitude * 8);
                return null;

            case IngameEffectKind.Strobe:
                // hard on/off bursts of the tubes (18 Hz square wave)
                var strobeOn = (long)(t * 18) % 2 == 0;
                var floor = 1f - (rule.Dip > 0 ? rule.Dip : 0.75f) * envelope;
                if (!strobeOn) { i1 *= floor; i2 *= floor; }
                return null;

            case IngameEffectKind.Tint:
                // sustained soft color grade, no dip — the glass takes the color
                return (rule, envelope * 0.6f);

            default:
                if (rule.Dip > 0)
                {
                    i1 *= 1f - rule.Dip * envelope;
                    i2 *= 1f - rule.Dip * envelope;
                }
                return (rule, envelope);
        }
    }

    private float _shakeX, _shakeY;

    /// <summary>Colored glass veil (flash) or additive pulse over the whole marquee.</summary>
    private void DrawIngameEffect(SKCanvas canvas, IngameEffectRule rule, float envelope, float w, float h, SKPoint offset)
    {
        var alpha = rule.Kind == IngameEffectKind.Pulse ? 0.28f : 0.38f;
        _glassPaint.BlendMode = rule.Kind == IngameEffectKind.Pulse ? SKBlendMode.Plus : SKBlendMode.SrcOver;
        _glassPaint.Color = rule.Color.WithAlpha((byte)(alpha * envelope * 255));
        canvas.DrawRect(offset.X, offset.Y, w, h, _glassPaint);
        _glassPaint.Color = SKColors.White;
        _glassPaint.BlendMode = SKBlendMode.SrcOver;
    }

    /// <summary>
    /// Lamp targets: live arcade outputs when present, otherwise the DOF attract
    /// pattern while browsing (chase, or alternate for a 2-lamp beacon pair).
    /// </summary>
    private void UpdateLamps(double t)
    {
        if (_lampStates.Length == 0 || _lampScene == null) return;

        if (_arcadeLive)
        {
            foreach (var state in _lampStates)
            {
                // a lamp may listen to several outputs (and one output may drive
                // several lamps): the strongest live value wins
                float? target = null;
                foreach (var pair in _lampScene.OutputMap)
                {
                    if (!pair.Value.Equals(state.Definition.Id, StringComparison.OrdinalIgnoreCase)) continue;
                    if (_arcadeOutputs.TryGetValue(pair.Key, out var value))
                        target = Math.Max(target ?? 0f, Math.Clamp(value, 0, 1));
                }
                if (target is { } resolved) state.Target = resolved;
            }
        }
        else if (!_ingame && t > 2.5)
        {
            // attract mode: show off the lamps without launching the game
            if (_lampStates.Length == 2)
            {
                var phase = t % 1.1 < 0.55;
                _lampStates[0].Target = phase ? 1f : 0f;
                _lampStates[1].Target = phase ? 0f : 1f;
            }
            else
            {
                var cycle = t % 10.0;
                if (cycle > 8.6) // periodic all-on pulse
                {
                    foreach (var state in _lampStates) state.Target = 1f;
                }
                else
                {
                    var active = (int)(t / 0.55) % _lampStates.Length;
                    for (var i = 0; i < _lampStates.Length; i++)
                        _lampStates[i].Target = i == active ? 1f : 0f;
                }
            }
        }
        else
        {
            foreach (var state in _lampStates) state.Target = 0f;
        }

        foreach (var state in _lampStates) state.Step();
    }

    /// <summary>Additive lamp glows: soft halo + brighter core, region-shaped or circular.</summary>
    private void DrawLamps(SKCanvas canvas)
    {
        if (_lampStates.Length == 0) return;
        var w = _maps!.Width;
        var h = _maps.Height;
        foreach (var state in _lampStates)
        {
            if (state.Current < 0.02f) continue;
            var def = state.Definition;
            var center = new SKPoint(_offset.X + def.X * w, _offset.Y + def.Y * h);
            var color = new SKColor(
                (byte)(def.ColorR * 255), (byte)(def.ColorG * 255), (byte)(def.ColorB * 255));

            if (def.Region is { } region)
            {
                var radius = region.Height * h * 0.62f;
                var stretch = Math.Max(0.6f, region.Width * w / (region.Height * h)) * 0.85f;
                DrawStretchedGlow(canvas, center, radius, stretch, 1f, color.WithAlpha((byte)(state.Current * 150)));
                DrawStretchedGlow(canvas, center, radius * 0.5f, stretch, 0.9f, color.WithAlpha((byte)(state.Current * 190)));
            }
            else
            {
                var radius = def.Radius * h * 2.0f;
                DrawStretchedGlow(canvas, center, radius, 1f, 1f, color.WithAlpha((byte)(state.Current * 140)));
                DrawStretchedGlow(canvas, center, radius * 0.42f, 1f, 1f, color.WithAlpha((byte)(state.Current * 200)));
            }
        }
    }

    /// <summary>
    /// Same tube state drives light and audio (§24). No permanent loops: hum/buzz
    /// only open during real instability (ignition, events); steady is silent.
    /// Ingame, everything is muted for a clean play session — cues are drained.
    /// </summary>
    private void SyncSound(float i1, float i2)
    {
        if (_sound == null || _tubes.Length == 0) return;

        if (_ingame)
        {
            _sound.SetLevels(0f, 0f);
            foreach (var tube in _tubes) tube.TakeCue();
            return;
        }

        var instability = 0f;
        foreach (var tube in _tubes)
            instability = Math.Max(instability, (float)tube.Instability);
        var lit = (_tubes.Length > 1 ? (i1 + i2) / 2f : i1);

        var active = instability > 0.12f || _sequence != null;
        _sound.SetLevels(
            active ? Math.Clamp(lit, 0f, 1f) * (float)_profile.Bulb.HumAmount : 0f,
            active ? instability * 0.5f : 0f);

        foreach (var tube in _tubes)
        {
            switch (tube.TakeCue())
            {
                case TubeLifeSimulator.SoundCue.Strike:
                    _sound.PlayOneShot("ampoule-on-off.mp3", 0.55f);
                    break;
                case TubeLifeSimulator.SoundCue.Ignited:
                    var soft = _profile.Bulb.SolidState || _profile.Bulb.Technology == BulbTechnology.Incandescent;
                    _sound.PlayOneShot(soft ? "ampoule-on.mp3" : "neon-on.mp3", soft ? 0.5f : 0.6f);
                    break;
                case TubeLifeSimulator.SoundCue.Popped:
                    _sound.PlayOneShot("neon-pop.mp3", 0.9f);
                    break;
            }
        }
    }

    /// <summary>shape[y] = cabinet ambient bounce + Σ tube glow × live intensity, in [0..1.2].</summary>
    private void UpdateShapeRow(float i1, float i2)
    {
        var h = _maps!.Height;
        _shapeBuffer ??= new byte[h * 4];
        var tubeCount = _tubeRow2 != null ? 2f : 1f;
        var ambient = _backlightProfile.Ambient *
                      Math.Clamp((i1 + (_tubeRow2 != null ? i2 : 0f)) / tubeCount, 0f, 1f);
        var gain = 1f - _backlightProfile.Ambient;

        for (var y = 0; y < h; y++)
        {
            var raw = ambient + gain * (_tubeRow1[y] * i1 + (_tubeRow2?[y] ?? 0f) * i2);
            var value = (byte)(Math.Clamp(raw, 0f, ShapeScale) / ShapeScale * 255f);
            var i = y * 4;
            _shapeBuffer[i] = value; _shapeBuffer[i + 1] = value; _shapeBuffer[i + 2] = value; _shapeBuffer[i + 3] = 255;
        }
        Marshal.Copy(_shapeBuffer, 0, _shapeRow!.GetPixels(), _shapeBuffer.Length);
        _shapeRow.NotifyPixelsChanged();
    }

    /// <summary>
    /// Additive glow at a tube's ends while it struggles to ignite: not a plain
    /// circle — an elongated halo along the tube axis plus a hot filament core,
    /// like a real electrode heating behind the glass.
    /// </summary>
    private void DrawElectrodeGlow(SKCanvas canvas, float electrode, float tubeY)
    {
        var w = _maps!.Width;
        var h = _maps.Height;
        var level = Math.Clamp(electrode, 0f, 1f);
        foreach (var tubeX in stackalloc[] { 0.045f, 0.955f })
        {
            var center = new SKPoint(_offset.X + tubeX * w, _offset.Y + tubeY * h);

            // compact soft halo, slightly elongated along the tube — discreet
            DrawStretchedGlow(canvas, center, 0.09f * h, 1.6f, 0.9f,
                new SKColor(255, 118, 66, (byte)(level * 100)));
            // small hot filament core
            DrawStretchedGlow(canvas, center, 0.04f * h, 1.3f, 0.8f,
                new SKColor(255, 205, 150, (byte)(level * 170)));
        }
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

    /// <summary>
    /// The physical tube, faintly visible through the art when it emits — its
    /// brightness follows the live intensity, so ignition flashes reveal the tube.
    /// </summary>
    private void DrawTubeVisual(SKCanvas canvas)
    {
        if (_tubeVisual == null || _tubeVisualOpacity <= 0 || _profile.Bulb.SolidState) return;
        var w = _maps!.Width;
        var h = _maps.Height;
        // preserve the tube's own aspect ratio — never deform it
        var tubeWidth = 0.94f * w;
        var tubeHeight = Math.Min(tubeWidth * _tubeVisual.Height / _tubeVisual.Width, 0.30f * h);
        tubeWidth = tubeHeight * _tubeVisual.Width / _tubeVisual.Height;
        for (var i = 0; i < _tubes.Length; i++)
        {
            var intensity = (float)Math.Clamp(_tubes[i].Intensity, 0, 1);
            if (intensity < 0.02f) continue;
            var tubeY = i == 0 ? _backlightProfile.TubeY1 : _backlightProfile.TubeY2;
            var centerY = _offset.Y + tubeY * h;
            var dest = SKRect.Create(_offset.X + (w - tubeWidth) / 2f, centerY - tubeHeight / 2f, tubeWidth, tubeHeight);
            _glowPaint.Color = SKColors.White.WithAlpha((byte)(intensity * _tubeVisualOpacity * 255));
            canvas.DrawBitmap(_tubeVisual, dest, _glowPaint);
        }
        _glowPaint.Color = SKColors.White;
    }

    /// <summary>
    /// Glass thickness in front of the print (§14 surfaceReflection): a diagonal
    /// sheen band plus a top curvature highlight, present even when the marquee is
    /// dark — the glass always reflects the room.
    /// </summary>
    private void DrawGlass(SKCanvas canvas)
    {
        if (_glassReflection <= 0) return;
        var rect = SKRect.Create(_offset.X, _offset.Y, _maps!.Width, _maps.Height);

        var band = (byte)(_glassReflection * 255 * 0.9f);
        using var sheen = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
            new[] { SKColors.White.WithAlpha(0), SKColors.White.WithAlpha(band), SKColors.White.WithAlpha(0) },
            new[] { 0.30f, 0.47f, 0.64f }, SKShaderTileMode.Clamp);
        _glassPaint.Shader = sheen;
        canvas.DrawRect(rect, _glassPaint);

        var top = (byte)(_glassReflection * 255 * 1.2f);
        using var curvature = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Left, rect.Top + rect.Height * 0.16f),
            new[] { SKColors.White.WithAlpha(top), SKColors.White.WithAlpha(0) },
            null, SKShaderTileMode.Clamp);
        _glassPaint.Shader = curvature;
        canvas.DrawRect(SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height * 0.16f), _glassPaint);
        _glassPaint.Shader = null;
    }

    private void StartGeneration(MarqueeRequest request, int surfaceWidth, int surfaceHeight)
    {
        _generating = true;
        Task.Run(() =>
        {
            try
            {
                var systemLogo = Path.GetFileName(request.Path).Contains("system-marquee", StringComparison.OrdinalIgnoreCase);

                // per-game lamp scene (rbmarquee.xml): converted DOF or generated from
                // outputs. A system logo never carries game lamps.
                var lampScene = !systemLogo && request.Meta?.Rom != null
                    ? RbMarqueeScene.Load(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "rbmarquee"),
                        request.Meta.Rom, _logger)
                    : null;

                // the DOF-calibrated artwork beats the scraped one when the scene
                // declares it: lamp regions were measured on that exact image
                var sourcePath = lampScene?.CalibratedImagePath ?? request.Path;

                using var source = SKBitmap.Decode(sourcePath);
                if (source == null || source.Width == 0 || source.Height == 0)
                {
                    _logger.LogWarning("Lighting: cannot decode marquee image {Path}; static image stays", sourcePath);
                    lock (_resultLock) { _generating = false; }
                    return;
                }

                // v0 heuristic until the WS contract carries imageSource (§5): APIExpose
                // names its upstream-composited images "generated-*"; scans keep "marquee.*".
                var fileName = Path.GetFileName(sourcePath);
                var composited = fileName.StartsWith("generated-", StringComparison.OrdinalIgnoreCase);
                var profile = _libraries.Resolve(request.Meta, composited);

                // content-aware framing: fill height without ever cutting the title;
                // system logos stay centered and modest; DOF lamps must all stay in frame
                (float Min, float Max)? lampSpan = null;
                if (lampScene is { Lamps.Count: > 0 })
                {
                    float min = 1f, max = 0f;
                    foreach (var lamp in lampScene.Lamps)
                    {
                        min = Math.Min(min, lamp.Region?.Left ?? lamp.X - lamp.Radius);
                        max = Math.Max(max, lamp.Region?.Right ?? lamp.X + lamp.Radius);
                    }
                    lampSpan = (Math.Clamp(min, 0f, 1f), Math.Clamp(max, 0f, 1f));
                }
                var framing = MarqueeFramer.Choose(source, surfaceWidth, surfaceHeight, _fillHeightMaxCrop, systemLogo, lampSpan);

                // lamp coordinates are normalized to the full artwork: remap them
                // into the cropped frame so they stay glued to their letters
                if (lampScene != null && framing.SourceCrop is { } crop)
                    lampScene = RemapForCrop(lampScene, crop, source.Width);
                var offset = new SKPoint((surfaceWidth - framing.Width) / 2f, (surfaceHeight - framing.Height) / 2f);
                var backlight = BacklightProfile.Resolve(profile.Bulb.SolidState,
                    (double)framing.Width / framing.Height);

                var started = System.Diagnostics.Stopwatch.StartNew();
                var maps = AutoMapGenerator.Generate(source, framing.Width, framing.Height, profile, framing.SourceCrop);
                _logger.LogInformation("Lighting maps for {Path}{Calibrated} at {W}x{H} in {Ms} ms — bulb {Bulb} (via {Source}), aging {Aging:F2}, {Tubes} tube(s), {Framing}{Lamps}",
                    fileName, sourcePath != request.Path ? " [image DOF calibrée]" : "",
                    framing.Width, framing.Height, started.ElapsedMilliseconds,
                    profile.Bulb.Id, profile.Source, profile.Aging, backlight.TwoTubes ? 2 : 1, framing.Label,
                    lampScene != null ? $", {lampScene.Lamps.Count} DOF lamp(s)" : "");

                lock (_resultLock)
                {
                    _pendingResult?.Item2.Dispose();
                    _pendingResult = (request.Path, maps, offset, surfaceWidth, surfaceHeight, profile, backlight, lampScene);
                    _generating = false;
                    _dirty = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lighting map generation failed for {Path}; static image stays", request.Path);
                lock (_resultLock) { _generating = false; }
            }
        });
    }

    private static RbMarqueeScene RemapForCrop(RbMarqueeScene scene, SKRectI crop, int sourceWidth)
    {
        var x0 = crop.Left / (float)sourceWidth;
        var scale = sourceWidth / (float)crop.Width;
        var lamps = scene.Lamps.Select(lamp => lamp with
        {
            X = (lamp.X - x0) * scale,
            Region = lamp.Region is { } region
                ? SKRect.Create((region.Left - x0) * scale, region.Top, region.Width * scale, region.Height)
                : null
        }).ToList();
        return new RbMarqueeScene
        {
            Rom = scene.Rom,
            Lamps = lamps,
            OutputMap = scene.OutputMap,
            AttractMode = scene.AttractMode
        };
    }

    private void AdoptPendingResult(TimeSpan elapsed)
    {
        (string, LightingMaps, SKPoint, int, int, ResolvedLightProfile, BacklightProfile, RbMarqueeScene?)? result;
        lock (_resultLock)
        {
            result = _pendingResult;
            _pendingResult = null;
        }
        if (result == null) return;

        ClearScene();
        var (path, maps, offset, w, h, profile, backlight, lampScene) = result.Value;
        _lampScene = lampScene;
        _lampStates = lampScene?.Lamps.Select(def => new LampState { Definition = def }).ToArray()
                      ?? Array.Empty<LampState>();
        _currentPath = path;
        _maps = maps;
        _offset = offset;
        _surfaceWidth = w;
        _surfaceHeight = h;
        _profile = profile;
        _backlightProfile = backlight;
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);
        _unlitShader = maps.Unlit.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
        _litShader = maps.Lit.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
        (_tubeRow1, _tubeRow2) = AutoMapGenerator.ComputeTubeRows(maps.Height, backlight);
        _shapeRow = new SKBitmap(new SKImageInfo(1, maps.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        _shapeBuffer = null;
        _sceneReadyAt = elapsed.TotalSeconds;
        // sceneless overlay sprites live on the absolute clock; the scene clock
        // restarts here, so their timestamps would be meaningless
        _sprites.Clear();
        CreateTubeSimulators();
    }

    /// <summary>
    /// One life per tube: independent random ignition scenario and events; in a
    /// pair, the second tube may start late, and only one may ever die. Also used
    /// by PowerCycle to restart the scene with fresh scenarios.
    /// </summary>
    private void CreateTubeSimulators()
    {
        var rng = new Random();
        if (_backlightProfile.TwoTubes)
        {
            TubeLifeSimulator? first = null, second = null;
            first = new TubeLifeSimulator(_profile.Bulb, rng, 0, () => second is not { IsDead: true });
            var delay = rng.NextDouble() < 0.5 ? 0 : 0.2 + rng.NextDouble() * 1.3;
            second = new TubeLifeSimulator(_profile.Bulb, rng, delay, () => !first.IsDead);
            _tubes = new[] { first, second };
        }
        else
        {
            // a lone tube dying leaves the marquee unlit-but-readable; keep it rare
            _tubes = new[] { new TubeLifeSimulator(_profile.Bulb, rng, 0, () => rng.NextDouble() < 0.35) };
        }
    }

    private void ClearScene()
    {
        _unlitShader?.Dispose();
        _litShader?.Dispose();
        _unlitShader = _litShader = null;
        _shapeRow?.Dispose();
        _shapeRow = null;
        _shapeBuffer = null;
        _maps?.Dispose();
        _maps = null;
        _currentPath = null;
        _tubes = Array.Empty<TubeLifeSimulator>();
        _lampScene = null;
        _lampStates = Array.Empty<LampState>();
        _arcadeOutputs.Clear();
        _arcadeLive = false;
        if (_sequence != null) { _sound?.StopSequence(_sequence); _sequence = null; }
        _nextSequenceAt = double.MaxValue;
        _blackoutUntil = -1;
        _sprites.Clear();
        lock (_fxLock) { _activeFx = null; _pendingSprites.Clear(); }
    }

    public void Dispose()
    {
        ClearScene();
        lock (_resultLock)
        {
            _pendingResult?.Item2.Dispose();
            _pendingResult = null;
        }
        _paint.Dispose();
        _glowPaint.Dispose();
        _glassPaint.Dispose();
        _tubeVisual?.Dispose();
        _effect.Dispose();
    }
}
