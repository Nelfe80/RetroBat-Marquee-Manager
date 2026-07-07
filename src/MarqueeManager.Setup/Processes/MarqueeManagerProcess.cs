using System.Diagnostics;
using System.IO;

namespace MarqueeManager.Setup.Processes;

/// <summary>
/// Detects and controls the MarqueeManager runtime. The setup stops it before
/// changing screens/DMD (both fight for the same windows and the same serial port)
/// and offers to restart it once the configuration is saved.
/// </summary>
public static class MarqueeManagerProcess
{
    private const string ProcessName = "MarqueeManager";

    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        var running = processes.Length > 0;
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return running;
    }

    public static void Stop()
    {
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            catch
            {
                // already gone
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public static bool Start(string pluginRoot)
    {
        var exe = Path.Combine(pluginRoot, "MarqueeManager.exe");
        if (!File.Exists(exe))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = pluginRoot, UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
