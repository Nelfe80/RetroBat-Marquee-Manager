using System.IO;
using System.Text.RegularExpressions;

namespace MarqueeManager.Setup.Localization;

/// <summary>
/// Two-language UI strings: `--lang fr|en` argument, then the persisted choice
/// (state\setup.ini [Setup] Language, written by the sidebar toggle), then the
/// RetroBat/EmulationStation language (es_settings.cfg), then the Windows UI
/// culture. Call sites keep both texts inline: L.T("texte", "text").
/// </summary>
public static class L
{
    public static bool French { get; private set; } = ResolveFrench();

    public static string T(string fr, string en) => French ? fr : en;

    /// <summary>Applies the persisted language once the plugin root is known
    /// (the CLI argument still wins — it drives the wiki screenshot runs).</summary>
    public static void Initialize(string? pluginRoot)
    {
        if (HasLangArgument())
        {
            return;
        }

        var saved = Config.SetupPrefs.Read(pluginRoot, "Language", "");
        if (saved.Length > 0)
        {
            French = saved.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Toggle(string? pluginRoot)
    {
        French = !French;
        Config.SetupPrefs.Write(pluginRoot, "Language", French ? "fr" : "en");
    }

    private static bool HasLangArgument()
        => Environment.GetCommandLineArgs().Any(arg =>
            arg.Equals("--lang", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase));

    private static bool ResolveFrench()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg.Equals("--lang", StringComparison.OrdinalIgnoreCase))
            {
                var index = Array.IndexOf(Environment.GetCommandLineArgs(), arg);
                var args = Environment.GetCommandLineArgs();
                if (index >= 0 && index + 1 < args.Length)
                {
                    return args[index + 1].StartsWith("fr", StringComparison.OrdinalIgnoreCase);
                }
            }

            if (arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
            {
                return arg["--lang=".Length..].StartsWith("fr", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (TryReadEsLanguage() is { } esLanguage)
        {
            return esLanguage.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
        }

        return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("fr", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>RetroBat root is two levels above the plugin folder.</summary>
    private static string? TryReadEsLanguage()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var settings = Path.Combine(dir.FullName, "emulationstation", ".emulationstation", "es_settings.cfg");
                if (File.Exists(settings))
                {
                    var match = Regex.Match(File.ReadAllText(settings),
                        "name=\"Language\"\\s+value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    return match.Success ? match.Groups[1].Value : null;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // fall back to the Windows culture
        }

        return null;
    }
}
