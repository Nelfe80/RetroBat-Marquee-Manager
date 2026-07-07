using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarqueeManager.Setup.TouchProfile;

/// <summary>
/// Advanced surface profile (state\surfaces.profile.json) — the file the runtime
/// reads to make the instruction card touch-interactive. The setup owns files marked
/// generatedBy=MarqueeManagerSetup and refuses to silently overwrite anything else.
/// </summary>
public sealed class TouchProfileDocument
{
    public const string Generator = "MarqueeManagerSetup";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("generatedBy")]
    public string? GeneratedBy { get; set; } = Generator;

    [JsonPropertyName("surfaces")]
    public List<SurfaceProfile> Surfaces { get; set; } = new();

    public SurfaceProfile? Surface(string kind)
        => Surfaces.FirstOrDefault(s => string.Equals(s.Kind, kind, StringComparison.OrdinalIgnoreCase));

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static TouchProfileDocument LoadOrNew(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<TouchProfileDocument>(File.ReadAllText(path), Options) ?? new TouchProfileDocument();
            }
        }
        catch
        {
            // corrupt file: treated as foreign, the caller checks IsOwnedBySetup before saving
        }

        return new TouchProfileDocument();
    }

    public static bool IsOwnedBySetup(string path)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            var doc = JsonSerializer.Deserialize<TouchProfileDocument>(File.ReadAllText(path), Options);
            return doc?.GeneratedBy == Generator;
        }
        catch
        {
            return false;
        }
    }

    public void Save(string path)
    {
        GeneratedBy = Generator;
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

        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}

public sealed class SurfaceProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("touch")]
    public TouchSettings? Touch { get; set; }
}

public sealed class TouchSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>simple | center-toggle | dual-player | zones</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "simple";

    [JsonPropertyName("defaultCard")]
    public string? DefaultCard { get; set; }

    /// <summary>Return to the default card after this delay (0 = stay).</summary>
    [JsonPropertyName("returnToDefaultMs")]
    public int ReturnToDefaultMs { get; set; }

    [JsonPropertyName("zones")]
    public List<TouchZone> Zones { get; set; } = new();
}

public sealed class TouchZone
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>"x,y,w,h" in percent of the surface, e.g. "0,0,50%,100%".</summary>
    [JsonPropertyName("rect")]
    public string Rect { get; set; } = "0,0,100%,100%";

    [JsonPropertyName("tap")]
    public TouchAction? Tap { get; set; }

    /// <summary>Parses the percent rect into fractions (0..1). Tolerant of missing % signs.</summary>
    public bool TryGetFractions(out double x, out double y, out double w, out double h)
    {
        x = y = 0;
        w = h = 1;
        var parts = Rect.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        var values = new double[4];
        for (var i = 0; i < 4; i++)
        {
            var raw = parts[i].TrimEnd('%').Trim();
            if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out values[i]))
            {
                return false;
            }
        }

        x = values[0] / 100.0;
        y = values[1] / 100.0;
        w = values[2] / 100.0;
        h = values[3] / 100.0;
        return w > 0 && h > 0;
    }

    public static string RectFromFractions(double x, double y, double w, double h)
    {
        string P(double value) => Math.Round(value * 100, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return $"{P(x)},{P(y)},{P(w)}%,{P(h)}%";
    }
}

public sealed class TouchAction
{
    /// <summary>show-card | show-player-card | cycle-card | default-card</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "cycle-card";

    [JsonPropertyName("card")]
    public string? Card { get; set; }

    [JsonPropertyName("player")]
    public int? Player { get; set; }

    [JsonPropertyName("durationMs")]
    public int? DurationMs { get; set; }

    public string Describe() => Action switch
    {
        "show-card" => $"afficher la carte {Card ?? "?"}",
        "show-player-card" => $"carte du joueur {Player ?? 0}",
        "cycle-card" => "carte suivante",
        "default-card" => "retour carte par défaut",
        _ => Action
    };
}
