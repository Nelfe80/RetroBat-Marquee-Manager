using System.Text.Json.Serialization;

namespace MarqueeManager.Compositions.Core.Composition;

/// <summary>One layer of a marquee composition. Coordinates are fractions of the
/// target surface (0..1), so a project survives a marquee resolution change.
///
/// Lives in the DOMAIN because both sides need it: the Setup edits and writes it,
/// and the runtime now renders it too (a game gabarit used to exist only after its
/// sheet had been opened in the Setup, which on a real library meant "never").</summary>
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

    /// <summary>Locked: selectable but not movable/resizable (template fanart).</summary>
    [JsonPropertyName("locked")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Locked { get; set; }
    /// <summary>Hidden: kept in the project but not rendered nor exported.</summary>
    [JsonPropertyName("hidden")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Hidden { get; set; }
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
