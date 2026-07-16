using Microsoft.Extensions.Logging.Abstractions;

namespace RetroBatMarqueeManager.Application.Media;

/// <summary>
/// Headless pre-generation of the template cache (`MarqueeManager.exe
/// --render-templates arcade,nes` — "all" = every system with a template in its
/// chain). For each game of the APIExpose media library whose marquee chain
/// contains a template source, renders the missing cache PNGs so the runtime
/// never composes during ES navigation. Progress goes to stdout
/// (`PROGRESS done/total system/rom`, final `DONE rendered skipped errors`),
/// consumed by the Setup's progress dialog.
/// </summary>
public static class TemplateBatchRenderer
{
    public static int Run(string systemsCsv)
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var logger = NullLogger.Instance;
        var resolver = new CompositionChainResolver(baseDirectory, logger);
        var renderer = new CompositionTemplateRenderer(baseDirectory, logger);
        var mediaRoot = Path.GetFullPath(Path.Combine(baseDirectory, "..", "APIExpose", "media", "systems"));
        if (!Directory.Exists(mediaRoot))
        {
            Console.WriteLine("ERROR APIExpose media library not found");
            return 2;
        }

        var requested = systemsCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.ToLowerInvariant())
            .ToHashSet();
        var all = requested.Count == 0 || requested.Contains("all");

        // work list: (system, rom, templateId, fanart, logo, output)
        var jobs = new List<(string System, string Rom, string Template, string? Fanart, string? Logo, string Output)>();
        foreach (var systemDir in Directory.EnumerateDirectories(mediaRoot))
        {
            var system = Path.GetFileName(systemDir);
            if (!all && !requested.Contains(system.ToLowerInvariant())) continue;

            var templateId = FirstTemplateInChain(resolver, system);
            if (templateId == null) continue;
            var template = renderer.Find(templateId);
            if (template == null) continue;

            var gamesDir = Path.Combine(systemDir, "games");
            if (!Directory.Exists(gamesDir)) continue;
            foreach (var gameDir in Directory.EnumerateDirectories(gamesDir))
            {
                var rom = Path.GetFileName(gameDir);
                var output = Path.Combine(baseDirectory, "media", "marquees", ".cache",
                    SafeName(system), SafeName(rom) + "-" + SafeName(templateId) + ".png");
                if (File.Exists(output)) continue;

                var fanart = FirstOf(Path.Combine(gameDir, "artwork", "fanart.jpg"),
                    Path.Combine(gameDir, "artwork", "fanart.png"));
                var logo = FirstOf(Path.Combine(gameDir, "ui", "wheels", "wheel.png"));
                if (fanart == null && logo == null) continue;
                jobs.Add((system, rom, templateId, fanart, logo, output));
            }
        }

        Console.WriteLine($"TOTAL {jobs.Count}");
        var rendered = 0;
        var errors = 0;
        var done = 0;
        // Skia renders are CPU-bound: parallelize on half the cores
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, job =>
        {
            try
            {
                var template = renderer.Find(job.Template)!;
                if (renderer.Render(template, job.Fanart, job.Logo, job.Output))
                    Interlocked.Increment(ref rendered);
            }
            catch
            {
                Interlocked.Increment(ref errors);
            }
            var current = Interlocked.Increment(ref done);
            if (current % 25 == 0 || current == jobs.Count)
                Console.WriteLine($"PROGRESS {current}/{jobs.Count} {job.System}/{job.Rom}");
        });

        Console.WriteLine($"DONE {rendered} {jobs.Count - rendered - errors} {errors}");
        return errors > 0 && rendered == 0 ? 1 : 0;
    }

    /// <summary>The first template source of the system's marquee chain, if any.</summary>
    private static string? FirstTemplateInChain(CompositionChainResolver resolver, string system)
    {
        // resolve with a probe that records template requests without rendering
        string? found = null;
        resolver.TemplateMissing = (templateId, _, _, _) => found ??= templateId;
        var meta = new Lighting.LightingSceneMeta(null, null, null, null, system, "__probe__");
        resolver.Resolve("marquee", meta, false, _ => null);
        resolver.TemplateMissing = null;
        return found;
    }

    private static string? FirstOf(params string[] paths) => paths.FirstOrDefault(File.Exists);

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
