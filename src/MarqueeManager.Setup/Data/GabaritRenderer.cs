using MarqueeManager.Compositions.Core.Composition;
using System.IO;

namespace MarqueeManager.Setup.Data;

/// <summary>
/// Renders a surface's general template (gabarit) for one system, headless, via the
/// existing WPF composer: it loads the gabarit MarqueeProject, REMAPS each layer's
/// media by its AssetKey (fanart, wheel/logo, marquee…) to THIS system's media, and
/// renders to a per-surface cache. One layout thus serves every system. Must run on
/// the UI thread (WPF RenderTargetBitmap).
/// </summary>
public static class GabaritRenderer
{
    /// <summary>categoryRoot: "marquees" | "toppers" | "dmd".</summary>
    public static string CachePath(string pluginRoot, string categoryRoot, string surfaceId, string system)
        => Path.Combine(pluginRoot, "media", categoryRoot, ".cache", "surfaces", Safe(surfaceId), "systems", Safe(system) + ".png");

    /// <summary>Per-game render of the surface's game gabarit.</summary>
    public static string GameCachePath(string pluginRoot, string categoryRoot, string surfaceId, string system, string rom)
        => Path.Combine(pluginRoot, "media", categoryRoot, ".cache", "surfaces", Safe(surfaceId), "games", Safe(system), Safe(rom) + ".png");

    public static bool HasGabarit(string pluginRoot, string categoryRoot, string surfaceId, string scope)
    {
        var project = new MarqueeProjectStore(pluginRoot, categoryRoot, surfaceId)
            .LoadProject(GabaritIdentity.SystemId, scope);
        return project != null && project.Layers.Any(l => !l.Hidden);
    }

    /// <summary>Deletes every cached gabarit render of a surface — both the per-system
    /// and per-game renders (call after an edit so the next view regenerates).</summary>
    public static void InvalidateSurface(string pluginRoot, string categoryRoot, string surfaceId)
    {
        var dir = Path.Combine(pluginRoot, "media", categoryRoot, ".cache", "surfaces", Safe(surfaceId));
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* locked: the next render overwrites */ }
    }

    /// <summary>Renders the SYSTEM gabarit for one system → cache path, or null when
    /// there is no gabarit. The system's media (fanart/wheel/marquee) is provided by
    /// the caller (same lookup as the composer palette).</summary>
    public static string? RenderSystem(string pluginRoot, string categoryRoot, string surfaceId, string system,
        int targetWidth, int targetHeight, IReadOnlyList<GameAsset> systemAssets)
    {
        var project = new MarqueeProjectStore(pluginRoot, categoryRoot, surfaceId)
            .LoadProject(GabaritIdentity.SystemId, GabaritIdentity.SystemScope);
        if (project == null || !project.Layers.Any(l => !l.Hidden) || targetWidth <= 0 || targetHeight <= 0)
            return null;

        var mediaRoot = Path.GetFullPath(Path.Combine(pluginRoot, "..", "APIExpose", "media", "systems"));
        var remapped = Remap(project, systemAssets, system, targetWidth, targetHeight);
        var cache = CachePath(pluginRoot, categoryRoot, surfaceId, system);
        try
        {
            var composer = new Controls.MarqueeComposer(targetWidth, targetHeight, mediaRoot);
            composer.LoadProject(remapped);
            composer.RenderPng(cache);
        }
        catch
        {
            return null;
        }
        return File.Exists(cache) ? cache : null;
    }

    /// <summary>Renders the GAME gabarit for one game → cache path, or null when there
    /// is no game gabarit. The game's media is provided by the caller (composer palette).</summary>
    public static string? RenderGame(string pluginRoot, string categoryRoot, string surfaceId, string system, string rom,
        int targetWidth, int targetHeight, IReadOnlyList<GameAsset> gameAssets)
    {
        var project = new MarqueeProjectStore(pluginRoot, categoryRoot, surfaceId)
            .LoadProject(GabaritIdentity.SystemId, GabaritIdentity.GameScopeFor(system));
        if (project == null || !project.Layers.Any(l => !l.Hidden) || targetWidth <= 0 || targetHeight <= 0)
            return null;

        var mediaRoot = Path.GetFullPath(Path.Combine(pluginRoot, "..", "APIExpose", "media"));
        var remapped = Remap(project, gameAssets, rom, targetWidth, targetHeight);
        var cache = GameCachePath(pluginRoot, categoryRoot, surfaceId, system, rom);
        try
        {
            var composer = new Controls.MarqueeComposer(targetWidth, targetHeight, mediaRoot);
            composer.LoadProject(remapped);
            composer.RenderPng(cache);
        }
        catch
        {
            return null;
        }
        return File.Exists(cache) ? cache : null;
    }

    private static MarqueeProject Remap(MarqueeProject source, IReadOnlyList<GameAsset> assets, string system, int width, int height)
    {
        var project = new MarqueeProject
        {
            System = GabaritIdentity.SystemId,
            Rom = system,
            Width = width,
            Height = height,
            Background = source.Background
        };
        foreach (var layer in source.Layers)
        {
            var clone = new MarqueeLayer
            {
                Source = layer.Source,
                AssetKey = layer.AssetKey,
                X = layer.X, Y = layer.Y, Scale = layer.Scale, Rotation = layer.Rotation,
                Opacity = layer.Opacity, FlipH = layer.FlipH,
                Text = layer.Text, FontSize = layer.FontSize, TextColor = layer.TextColor, Bold = layer.Bold,
                Locked = layer.Locked, Hidden = layer.Hidden
            };
            // remap a media layer to THIS system's asset of the same type (fanart,
            // wheel/logo, marquee…); gradient/text/download layers keep their source
            var asset = assets.FirstOrDefault(a => a.Key.Equals(layer.AssetKey, StringComparison.OrdinalIgnoreCase));
            if (asset is not null && File.Exists(asset.Path)) clone.Source = asset.Path;
            project.Layers.Add(clone);
        }
        return project;
    }

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
