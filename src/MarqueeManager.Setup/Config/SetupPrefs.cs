using System.IO;

namespace MarqueeManager.Setup.Config;

/// <summary>
/// Setup-owned preferences (theme, language) in state\setup.ini. Kept out of
/// config.ini on purpose: the runtime's IniConfigService regenerates that file
/// and must stay its single owner.
/// </summary>
public static class SetupPrefs
{
    private const string Section = "Setup";

    public static string Read(string? pluginRoot, string key, string fallback)
    {
        try
        {
            return IniFile.Load(PathFor(pluginRoot)).Get(Section, key, fallback);
        }
        catch
        {
            return fallback;
        }
    }

    public static void Write(string? pluginRoot, string key, string value)
    {
        try
        {
            var path = PathFor(pluginRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var ini = IniFile.Load(path);
            ini.Set(Section, key, value);
            ini.Save();
        }
        catch
        {
            // the preference still applies for the session
        }
    }

    private static string PathFor(string? pluginRoot)
        => Path.Combine(pluginRoot ?? ".", "state", "setup.ini");
}
