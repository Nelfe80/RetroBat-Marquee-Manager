using System.IO;

namespace MarqueeManager.Setup.Config;

/// <summary>
/// Locates the MarqueeManager plugin root (the folder holding config.ini) by walking
/// up from the executable — works both published at the plugin root and from bin\Debug.
/// </summary>
public static class PluginPaths
{
    public static string FindPluginRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "config.ini"))
                && Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            // published at the plugin root: config.ini next to MarqueeManagerSetup.exe
            if (File.Exists(Path.Combine(dir.FullName, "config.ini"))
                && File.Exists(Path.Combine(dir.FullName, "MarqueeManager.exe")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    }

    public static string ConfigPath(string root) => Path.Combine(root, "config.ini");

    public static string TouchProfilePath(string root) => Path.Combine(root, "state", "surfaces.profile.json");
}
