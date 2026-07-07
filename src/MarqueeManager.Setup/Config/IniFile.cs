using System.IO;
using System.Text;

namespace MarqueeManager.Setup.Config;

/// <summary>
/// Line-preserving INI reader/editor: keeps every comment, blank line and ordering,
/// and only rewrites the keys it is told to. The runtime's config.ini is heavily
/// commented in French — those comments are user documentation and must survive
/// every save. Backs up to .bak before writing.
/// </summary>
public sealed class IniFile
{
    private readonly List<string> _lines;

    private IniFile(string path, List<string> lines)
    {
        Path = path;
        _lines = lines;
    }

    public string Path { get; }

    public static IniFile Load(string path)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        return new IniFile(path, lines);
    }

    public string Get(string section, string key, string fallback = "")
    {
        var start = FindSectionLine(section);
        if (start < 0)
        {
            return fallback;
        }

        var end = FindSectionEnd(start);
        for (var i = start + 1; i < end; i++)
        {
            if (TryParseKey(_lines[i], out var lineKey, out var value)
                && lineKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return fallback;
    }

    public bool GetBool(string section, string key, bool fallback)
        => bool.TryParse(Get(section, key, fallback.ToString()), out var value) ? value : fallback;

    public int GetInt(string section, string key, int fallback)
        => int.TryParse(Get(section, key, fallback.ToString()), out var value) ? value : fallback;

    public double GetDouble(string section, string key, double fallback)
        => double.TryParse(Get(section, key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value : fallback;

    /// <summary>
    /// Sets a key inside a section, preserving any inline comment already on that line.
    /// Un-comments a `;Key=...` line when the key only exists commented out (the runtime
    /// ships e.g. `;MarqueeBounds=` as a documented example). Creates the section and/or
    /// the key if missing.
    /// </summary>
    public void Set(string section, string key, string value)
    {
        var start = FindSectionLine(section);
        if (start < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0)
            {
                _lines.Add("");
            }

            _lines.Add($"[{section}]");
            _lines.Add($"{key}={value}");
            return;
        }

        var end = FindSectionEnd(start);
        var commentedAt = -1;
        for (var i = start + 1; i < end; i++)
        {
            if (TryParseKey(_lines[i], out var lineKey, out _)
                && lineKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var comment = ExtractInlineComment(_lines[i]);
                _lines[i] = $"{key}={value}{comment}";
                return;
            }

            if (commentedAt < 0 && IsCommentedKey(_lines[i], key))
            {
                commentedAt = i;
            }
        }

        if (commentedAt >= 0)
        {
            _lines[commentedAt] = $"{key}={value}";
            return;
        }

        // Key not found: insert at the end of the section (before trailing blank lines).
        var insertAt = end;
        while (insertAt - 1 > start && _lines[insertAt - 1].Trim().Length == 0)
        {
            insertAt--;
        }

        _lines.Insert(insertAt, $"{key}={value}");
    }

    /// <summary>
    /// Comments a key out instead of deleting it — the value stays visible as
    /// documentation (used for *Bounds when the user goes back to fullscreen).
    /// </summary>
    public void CommentOut(string section, string key)
    {
        var start = FindSectionLine(section);
        if (start < 0)
        {
            return;
        }

        var end = FindSectionEnd(start);
        for (var i = start + 1; i < end; i++)
        {
            if (TryParseKey(_lines[i], out var lineKey, out _)
                && lineKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                _lines[i] = ";" + _lines[i];
                return;
            }
        }
    }

    public void Save()
    {
        if (File.Exists(Path))
        {
            try
            {
                File.Copy(Path, Path + ".bak", overwrite: true);
            }
            catch
            {
                // best effort backup
            }
        }

        File.WriteAllText(Path, string.Join(Environment.NewLine, _lines) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static bool TryParseKey(string line, out string key, out string value)
    {
        key = "";
        value = "";
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#') || trimmed.StartsWith('['))
        {
            return false;
        }

        var eq = trimmed.IndexOf('=');
        if (eq <= 0)
        {
            return false;
        }

        key = trimmed[..eq].Trim();
        value = trimmed[(eq + 1)..].Trim();
        var comment = value.IndexOfAny(new[] { ';', '#' });
        // inline comments after the value are rare in this file; only strip when
        // separated by whitespace so paths like `.log\debug.log` stay intact
        if (comment > 0 && value[comment - 1] is ' ' or '\t')
        {
            value = value[..comment].TrimEnd();
        }

        return true;
    }

    private static bool IsCommentedKey(string line, string key)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith(';'))
        {
            return false;
        }

        trimmed = trimmed.TrimStart(';').TrimStart();
        var eq = trimmed.IndexOf('=');
        return eq > 0 && trimmed[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
    }

    private int FindSectionLine(string section)
    {
        var header = $"[{section}]";
        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].Trim().Equals(header, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindSectionEnd(int sectionStart)
    {
        for (var i = sectionStart + 1; i < _lines.Count; i++)
        {
            if (_lines[i].TrimStart().StartsWith('['))
            {
                return i;
            }
        }

        return _lines.Count;
    }

    private static string ExtractInlineComment(string line)
    {
        var eq = line.IndexOf('=');
        if (eq < 0)
        {
            return "";
        }

        for (var i = eq + 1; i < line.Length; i++)
        {
            if ((line[i] == ';' || line[i] == '#') && i > 0 && line[i - 1] is ' ' or '\t')
            {
                return "  " + line[i..];
            }
        }

        return "";
    }
}
