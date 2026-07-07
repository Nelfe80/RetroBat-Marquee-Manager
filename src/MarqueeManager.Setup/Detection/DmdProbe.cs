using System.IO;
using System.IO.Ports;
using MarqueeManager.Setup.Localization;

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
            DmdDeviceDllFound ? L.T("DmdDevice.dll : présent", "DmdDevice.dll: present") : L.T("DmdDevice.dll : introuvable", "DmdDevice.dll: not found"),
            ZeDmdDllFound ? L.T("Librairie ZeDMD : présente", "ZeDMD library: present") : L.T("Librairie ZeDMD : introuvable", "ZeDMD library: not found"),
            DmdExtFound ? L.T("dmdext.exe : présent", "dmdext.exe: present") : L.T("dmdext.exe : introuvable", "dmdext.exe: not found"),
            SerialPorts.Count > 0
                ? L.T("Ports série : ", "Serial ports: ") + string.Join(", ", SerialPorts)
                : L.T("Ports série : aucun détecté", "Serial ports: none detected")
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
