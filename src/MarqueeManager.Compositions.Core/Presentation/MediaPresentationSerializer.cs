using System.Text.Json;
using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Policy;
using MarqueeManager.Compositions.Core.Resolution;

namespace MarqueeManager.Compositions.Core.Presentation;

/// <summary>
/// Parses and writes the media-presentation document as a STRING (no file I/O —
/// the Setup and runtime own the file). Only non-null delta terminals are written,
/// so reverting a field to inherit means the field simply disappears (§20.1).
/// </summary>
public static class MediaPresentationSerializer
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    // ---------------- parse ----------------

    /// <summary>Null when the text is absent, malformed or a wrong schema — the
    /// caller then falls back to the pure defaults.</summary>
    public static MediaPresentationDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Str(root, "schema") != MediaPresentationDocument.SchemaId) return null;

            var surfaces = new Dictionary<string, SurfaceScopeDeltas>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("surfaces", out var surfacesEl) && surfacesEl.ValueKind == JsonValueKind.Object)
                foreach (var prop in surfacesEl.EnumerateObject())
                    surfaces[prop.Name] = new SurfaceScopeDeltas(
                        prop.Value.TryGetProperty("system", out var s) ? ParseScopeDelta(s) : null,
                        prop.Value.TryGetProperty("game", out var g) ? ParseScopeDelta(g) : null);

            var targets = new List<TargetPolicy>();
            if (root.TryGetProperty("targetPolicies", out var tps) && tps.ValueKind == JsonValueKind.Array)
                foreach (var tp in tps.EnumerateArray())
                    if (ParseTargetPolicy(tp) is { } parsed)
                        targets.Add(parsed);

            return new MediaPresentationDocument(surfaces, targets);
        }
        catch
        {
            return null;
        }
    }

    private static TargetPolicy? ParseTargetPolicy(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var surfaceId = Str(el, "surfaceId");
        if (surfaceId.Length == 0) return null;
        var scope = Str(el, "scope").Equals("game", OIC) ? MediaScope.Game : MediaScope.System;
        return new TargetPolicy(scope, surfaceId,
            NullIfEmpty(Str(el, "frontendSystem")),
            NullIfEmpty(Str(el, "canonicalSystem")),
            NullIfEmpty(Str(el, "gameId")),
            NullIfEmpty(Str(el, "rom")),
            ParseScopeDelta(el));
    }

    private static ScopePolicyDelta ParseScopeDelta(JsonElement el)
    {
        Dictionary<SourceKind, SourceSettingsDelta>? sources = null;
        if (el.TryGetProperty("sources", out var srcs) && srcs.ValueKind == JsonValueKind.Object)
        {
            sources = new Dictionary<SourceKind, SourceSettingsDelta>();
            foreach (var p in srcs.EnumerateObject())
                if (TryParseSourceKind(p.Name, out var kind))
                    sources[kind] = ParseSourceDelta(p.Value);
            if (sources.Count == 0) sources = null;
        }
        return new ScopePolicyDelta(
            OptStr(el, "templateId"),
            OptBool(el, "autoGenerate"),
            sources,
            el.TryGetProperty("neutralBackground", out var nb) ? ParseBackgroundDelta(nb) : null);
    }

    private static SourceSettingsDelta ParseSourceDelta(JsonElement el)
        => new(
            OptBool(el, "enabled"),
            el.TryGetProperty("fit", out var f) ? ParseFitDelta(f) : null,
            el.TryGetProperty("logoLayout", out var l) ? ParseLogoDelta(l) : null);

    private static FitPolicyDelta ParseFitDelta(JsonElement el)
        => new(
            el.TryGetProperty("mode", out var m) && TryParseFitMode(m.GetString(), out var mode) ? mode : null,
            el.TryGetProperty("alignX", out var ax) && TryParseHAlign(ax.GetString(), out var hx) ? hx : null,
            el.TryGetProperty("alignY", out var ay) && TryParseVAlign(ay.GetString(), out var vy) ? vy : null,
            OptDouble(el, "maxCrop"),
            el.TryGetProperty("fallback", out var fb) && TryParseFitMode(fb.GetString(), out var fbm) ? fbm : null);

    private static LogoLayoutDelta ParseLogoDelta(JsonElement el)
        => new(
            OptDouble(el, "paddingX"),
            OptDouble(el, "paddingY"),
            OptDouble(el, "minimumPadding"),
            el.TryGetProperty("background", out var b) ? ParseBackgroundDelta(b) : null);

    private static BackgroundSpecDelta ParseBackgroundDelta(JsonElement el)
        => new(OptStr(el, "kind"), OptStr(el, "color"));

    // ---------------- serialize ----------------

    public static string Serialize(MediaPresentationDocument document)
    {
        var root = new Dictionary<string, object?>
        {
            ["schema"] = MediaPresentationDocument.SchemaId,
            ["generatedBy"] = MediaPresentationDocument.Generator,
            ["surfaces"] = document.Surfaces.ToDictionary(kv => kv.Key, kv =>
            {
                var o = new Dictionary<string, object?>();
                if (kv.Value.System is { } sys) o["system"] = WriteScopeDelta(sys);
                if (kv.Value.Game is { } game) o["game"] = WriteScopeDelta(game);
                return (object?)o;
            }),
            ["targetPolicies"] = document.TargetPolicies.Select(WriteTargetPolicy).ToList()
        };
        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }

    private static Dictionary<string, object?> WriteTargetPolicy(TargetPolicy tp)
    {
        var o = new Dictionary<string, object?>
        {
            ["scope"] = tp.Scope == MediaScope.Game ? "game" : "system",
            ["surfaceId"] = tp.SurfaceId
        };
        if (tp.FrontendSystem is { } fs) o["frontendSystem"] = fs;
        if (tp.CanonicalSystem is { } cs) o["canonicalSystem"] = cs;
        if (tp.GameId is { } gi) o["gameId"] = gi;
        if (tp.Rom is { } rom) o["rom"] = rom;
        foreach (var (key, value) in WriteScopeDelta(tp.Delta)) o[key] = value;
        return o;
    }

    private static Dictionary<string, object?> WriteScopeDelta(ScopePolicyDelta d)
    {
        var o = new Dictionary<string, object?>();
        if (d.TemplateId is { } t) o["templateId"] = t;
        if (d.AutoGenerate is { } a) o["autoGenerate"] = a;
        if (d.Sources is { Count: > 0 })
            o["sources"] = d.Sources.ToDictionary(kv => SourceKindName(kv.Key), kv => (object?)WriteSourceDelta(kv.Value));
        if (d.NeutralBackground is { } nb) o["neutralBackground"] = WriteBackground(nb);
        return o;
    }

    private static Dictionary<string, object?> WriteSourceDelta(SourceSettingsDelta d)
    {
        var o = new Dictionary<string, object?>();
        if (d.Enabled is { } e) o["enabled"] = e;
        if (d.Fit is { } f) o["fit"] = WriteFit(f);
        if (d.LogoLayout is { } l) o["logoLayout"] = WriteLogo(l);
        return o;
    }

    private static Dictionary<string, object?> WriteFit(FitPolicyDelta d)
    {
        var o = new Dictionary<string, object?>();
        if (d.Mode is { } m) o["mode"] = FitModeName(m);
        if (d.AlignX is { } ax) o["alignX"] = ax.ToString().ToLowerInvariant();
        if (d.AlignY is { } ay) o["alignY"] = ay.ToString().ToLowerInvariant();
        if (d.MaxCrop is { } mc) o["maxCrop"] = mc;
        if (d.Fallback is { } fb) o["fallback"] = FitModeName(fb);
        return o;
    }

    private static Dictionary<string, object?> WriteLogo(LogoLayoutDelta d)
    {
        var o = new Dictionary<string, object?>();
        if (d.PaddingX is { } px) o["paddingX"] = px;
        if (d.PaddingY is { } py) o["paddingY"] = py;
        if (d.MinimumPadding is { } mp) o["minimumPadding"] = mp;
        if (d.Background is { } b) o["background"] = WriteBackground(b);
        return o;
    }

    private static Dictionary<string, object?> WriteBackground(BackgroundSpecDelta d)
    {
        var o = new Dictionary<string, object?>();
        if (d.Kind is { } k) o["kind"] = k;
        if (d.Color is { } c) o["color"] = c;
        return o;
    }

    // ---------------- enum <-> string ----------------

    private static string FitModeName(FitMode m) => m switch
    {
        FitMode.Contain => "contain",
        FitMode.Cover => "cover",
        FitMode.FillHeight => "fill-height",
        FitMode.FillWidth => "fill-width",
        FitMode.Dynamic => "dynamic",
        _ => "contain"
    };

    private static bool TryParseFitMode(string? s, out FitMode mode)
    {
        mode = FitMode.Contain;
        switch (s?.ToLowerInvariant())
        {
            case "contain": mode = FitMode.Contain; return true;
            case "cover": mode = FitMode.Cover; return true;
            case "fill-height": mode = FitMode.FillHeight; return true;
            case "fill-width": mode = FitMode.FillWidth; return true;
            case "dynamic": mode = FitMode.Dynamic; return true;
            default: return false;
        }
    }

    private static bool TryParseHAlign(string? s, out HAlign a)
    {
        a = HAlign.Auto;
        switch (s?.ToLowerInvariant())
        {
            case "left": a = HAlign.Left; return true;
            case "center": a = HAlign.Center; return true;
            case "right": a = HAlign.Right; return true;
            case "auto": a = HAlign.Auto; return true;
            default: return false;
        }
    }

    private static bool TryParseVAlign(string? s, out VAlign a)
    {
        a = VAlign.Auto;
        switch (s?.ToLowerInvariant())
        {
            case "top": a = VAlign.Top; return true;
            case "center": a = VAlign.Center; return true;
            case "bottom": a = VAlign.Bottom; return true;
            case "auto": a = VAlign.Auto; return true;
            default: return false;
        }
    }

    private static string SourceKindName(SourceKind k) => k switch
    {
        SourceKind.Personal => "personal",
        SourceKind.UserDrop => "userDrop",
        SourceKind.Generated => "generated",
        SourceKind.Scraped => "scraped",
        SourceKind.Logo => "logo",
        SourceKind.SystemFallback => "systemFallback",
        SourceKind.Dynamic => "dynamic",
        _ => k.ToString().ToLowerInvariant()
    };

    private static bool TryParseSourceKind(string name, out SourceKind kind)
    {
        switch (name.ToLowerInvariant())
        {
            case "personal": kind = SourceKind.Personal; return true;
            case "userdrop":
            case "user-drop": kind = SourceKind.UserDrop; return true;
            case "generated": kind = SourceKind.Generated; return true;
            case "scraped": kind = SourceKind.Scraped; return true;
            case "logo": kind = SourceKind.Logo; return true;
            case "systemfallback":
            case "system-fallback": kind = SourceKind.SystemFallback; return true;
            case "dynamic": kind = SourceKind.Dynamic; return true;
            default: kind = SourceKind.Personal; return false;
        }
    }

    // ---------------- json helpers ----------------

    private static string Str(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static string? OptStr(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? OptBool(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : null;

    private static double? OptDouble(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
