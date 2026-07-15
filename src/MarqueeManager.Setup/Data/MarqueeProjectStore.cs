using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarqueeManager.Setup.Data;

/// <summary>One layer of a marquee composition. Coordinates are fractions of the
/// target surface (0..1), so a project survives a marquee resolution change.</summary>
public sealed class MarqueeLayer
{
    [JsonPropertyName("source")] public string Source { get; set; } = "";      // path relative to the APIExpose media root, or "text"
    [JsonPropertyName("assetKey")] public string AssetKey { get; set; } = ""; // fanart / wheel / … (palette display)
    [JsonPropertyName("x")] public double X { get; set; } = 0.5;               // center, fraction of width
    [JsonPropertyName("y")] public double Y { get; set; } = 0.5;               // center, fraction of height
    [JsonPropertyName("scale")] public double Scale { get; set; } = 1.0;       // 1 = fits the surface height
    [JsonPropertyName("rotation")] public double Rotation { get; set; }        // degrees
    [JsonPropertyName("opacity")] public double Opacity { get; set; } = 1.0;
    [JsonPropertyName("flipH")] public bool FlipH { get; set; }

    // text layer only
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("fontSize")] public double FontSize { get; set; } = 0.3; // fraction of surface height
    [JsonPropertyName("color")] public string TextColor { get; set; } = "#FFFFFF";
    [JsonPropertyName("bold")] public bool Bold { get; set; } = true;
}

/// <summary>Composition background: solid color, two-color gradient, or a media
/// stretched and blurred behind the layers.</summary>
public sealed class MarqueeBackground
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "solid";     // solid | gradient | media
    [JsonPropertyName("color")] public string Color { get; set; } = "#000000";
    [JsonPropertyName("color2")] public string Color2 { get; set; } = "#202038";
    [JsonPropertyName("source")] public string? Source { get; set; }           // media path (relative) for kind=media
    [JsonPropertyName("blur")] public double Blur { get; set; } = 12;
}

/// <summary>Editable project behind a composed marquee (saved next to the PNG).</summary>
public sealed class MarqueeProject
{
    public const string Schema = "marqueemanager.marquee-project.v1";
    public const string Generator = "MarqueeManagerSetup";

    [JsonPropertyName("schema")] public string SchemaVersion { get; set; } = Schema;
    [JsonPropertyName("generatedBy")] public string GeneratedBy { get; set; } = Generator;
    [JsonPropertyName("system")] public string System { get; set; } = "";
    [JsonPropertyName("rom")] public string Rom { get; set; } = "";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("background")] public MarqueeBackground Background { get; set; } = new();
    [JsonPropertyName("layers")] public List<MarqueeLayer> Layers { get; set; } = new();
}

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

    public MarqueeProjectStore(string pluginRoot)
    {
        _root = Path.Combine(pluginRoot, "media", "marquees");
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
