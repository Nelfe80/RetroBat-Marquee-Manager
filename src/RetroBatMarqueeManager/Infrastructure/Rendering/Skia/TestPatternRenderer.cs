using SkiaSharp;

namespace RetroBatMarqueeManager.Infrastructure.Rendering.Skia;

/// <summary>
/// Phase 0 validation renderer: a single SKSL runtime effect combining a dark
/// "unlit" base, a breathing/flickering backlight and two crossing colored lamps
/// mixed additively then tonemapped (Reinhard). Proves SKRuntimeEffect works on
/// the CPU raster backend and previews the §17.2 composition formula.
/// </summary>
public sealed class TestPatternRenderer : ISkiaFrameRenderer
{
    private const string Sksl = @"
uniform float2 iResolution;
uniform float  iTime;

float gauss(float2 uv, float2 center, float radius) {
    float2 aspect = float2(iResolution.x / iResolution.y, 1.0);
    float d = distance(uv * aspect, center * aspect);
    return exp(-(d * d) / (2.0 * radius * radius));
}

float hash(float2 p) {
    return fract(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

half4 main(float2 fragCoord) {
    float2 uv = fragCoord / iResolution;

    // unlit base plate: dark warm gradient
    float3 unlit = float3(0.06, 0.055, 0.05) * (0.7 + 0.6 * uv.y);

    // uniform backlight: slow breathing + subtle electrical flicker
    float flicker = 1.0 + 0.03 * (hash(float2(floor(iTime * 24.0), 1.0)) - 0.5);
    float backlight = (0.22 + 0.10 * sin(iTime * 0.7)) * flicker;
    float3 light = float3(1.0, 0.92, 0.78) * backlight;

    // two crossing colored lamps, additive (red/blue beacon pre-validation)
    float2 redPos  = float2(0.5 + 0.28 * sin(iTime * 1.1), 0.5);
    float2 bluePos = float2(0.5 - 0.28 * sin(iTime * 1.1), 0.5);
    light += float3(1.0, 0.15, 0.10) * (1.4 * gauss(uv, redPos, 0.10));
    light += float3(0.15, 0.25, 1.0) * (1.4 * gauss(uv, bluePos, 0.10));

    // compose + Reinhard tonemap: overlap must not clip to white
    float3 color = unlit + light;
    color = color / (1.0 + color);

    return half4(half3(color), 1.0);
}";

    private readonly SKRuntimeEffect _effect;
    private readonly SKPaint _paint = new();

    public TestPatternRenderer()
    {
        _effect = SKRuntimeEffect.CreateShader(Sksl, out var errors)
                  ?? throw new InvalidOperationException($"SKSL compilation failed: {errors}");
    }

    public void Render(SKCanvas canvas, int width, int height, TimeSpan elapsed)
    {
        var uniforms = new SKRuntimeEffectUniforms(_effect)
        {
            ["iResolution"] = new[] { (float)width, (float)height },
            ["iTime"] = (float)elapsed.TotalSeconds
        };
        using var shader = _effect.ToShader(uniforms);
        _paint.Shader = shader;
        canvas.DrawRect(0, 0, width, height, _paint);
        _paint.Shader = null;
    }

    public void Dispose()
    {
        _paint.Dispose();
        _effect.Dispose();
    }
}
