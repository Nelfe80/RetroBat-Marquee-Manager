using System.IO;
using System.IO.Ports;

namespace MarqueeManager.Setup.Detection;

public sealed record DmdProbeResult(
    bool DmdDeviceDllFound,
    bool ZeDmdDllFound,
    bool DmdExtFound,
    IReadOnlyList<string> SerialPorts,
    string ToolsFolder)
{
    public string Describe()
    {
        var lines = new List<string>
        {
            DmdDeviceDllFound ? "DmdDevice.dll : présent" : "DmdDevice.dll : introuvable",
            ZeDmdDllFound ? "Librairie ZeDMD : présente" : "Librairie ZeDMD : introuvable",
            DmdExtFound ? "dmdext.exe : présent" : "dmdext.exe : introuvable",
            SerialPorts.Count > 0
                ? "Ports série : " + string.Join(", ", SerialPorts)
                : "Ports série : aucun détecté"
        };
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Static inventory of the private DMD stack (tools\dmd) and the serial ports. The
/// probe never opens the DMD itself: pushing frames stays an explicit user action,
/// so an unplugged or powered-off panel never blocks the setup.
/// </summary>
public static class DmdProbe
{
    public static DmdProbeResult Inspect(string pluginRoot)
    {
        var tools = Path.Combine(pluginRoot, "tools", "dmd");
        var hasFolder = Directory.Exists(tools);

        bool Any(params string[] patterns)
            => hasFolder && patterns.Any(p => Directory.EnumerateFiles(tools, p).Any());

        string[] ports;
        try
        {
            ports = SerialPort.GetPortNames().Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch
        {
            ports = Array.Empty<string>();
        }

        return new DmdProbeResult(
            DmdDeviceDllFound: Any("DmdDevice*.dll"),
            ZeDmdDllFound: Any("zedmd*.dll", "ZeDMD*.dll", "libzedmd*.dll"),
            DmdExtFound: hasFolder && File.Exists(Path.Combine(tools, "dmdext.exe")),
            SerialPorts: ports,
            ToolsFolder: tools);
    }
}
