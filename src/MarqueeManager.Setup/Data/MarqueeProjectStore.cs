using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

// The project model itself lives in the domain (Compositions.Core) — the
// runtime renders these projects too, so the contract cannot live here.
using MarqueeManager.Compositions.Core.Composition;

namespace MarqueeManager.Setup.Data;

/// <summary>
/// Storage of user marquee compositions on the MarqueeManager side:
/// media\marquees\&lt;system&gt;\&lt;rom&gt;.png (what the runtime displays, absolute
/// priority over scraped/generated) + &lt;rom&gt;.project.json (re-editable layers).
/// Same ownership guard as the touch profile: a JSON another tool wrote is
/// never overwritten silently.
/// </summary>
public sealed class MarqueeProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _root;

    /// <summary>category: "marquees" (default), "toppers" or "dmd" — the media
    /// folder the runtime's chains read for that surface family. surfaceId set =
    /// the creation belongs to THAT surface only
    /// (media\&lt;cat&gt;\surfaces\&lt;surfaceId&gt;\…) : creation A on surface 1 and
    /// creation B on surface 2 can coexist for the same game or system.</summary>
    public MarqueeProjectStore(string pluginRoot, string category = "marquees", string? surfaceId = null)
    {
        _root = surfaceId is { Length: > 0 }
            ? Path.Combine(pluginRoot, "media", category, "surfaces", SafeName(surfaceId))
            : Path.Combine(pluginRoot, "media", category);
    }

    public string PngPath(string system, string rom)
        => Path.Combine(_root, SafeName(system), SafeName(rom) + ".png");

    public string ProjectPath(string system, string rom)
        => Path.Combine(_root, SafeName(system), SafeName(rom) + ".project.json");

    public bool HasComposition(string system, string rom) => File.Exists(PngPath(system, rom));

    public MarqueeProject? LoadProject(string system, string rom)
    {
        try
        {
            var path = ProjectPath(system, rom);
            if (!File.Exists(path))
            {
                return null;
            }

            var project = JsonSerializer.Deserialize<MarqueeProject>(File.ReadAllText(path));
            return project?.SchemaVersion == MarqueeProject.Schema ? project : null;
        }
        catch
        {
            return null;
        }
    }

    public bool IsOwnedBySetup(string system, string rom)
    {
        var path = ProjectPath(system, rom);
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("generatedBy", out var by)
                   && by.GetString() == MarqueeProject.Generator;
        }
        catch
        {
            // corrupt/foreign file: treat as not owned
            return false;
        }
    }

    /// <summary>Writes the project JSON; the PNG render is written by the composer.</summary>
    public void SaveProject(MarqueeProject project)
    {
        var path = ProjectPath(project.System, project.Rom);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            try
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }
            catch
            {
                // best effort backup
            }
        }

        File.WriteAllText(path, JsonSerializer.Serialize(project, JsonOptions));
    }

    public void Delete(string system, string rom)
    {
        foreach (var path in new[] { PngPath(system, rom), ProjectPath(system, rom) })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // locked file: the next save will overwrite it
            }
        }
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
