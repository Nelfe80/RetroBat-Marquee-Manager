using System.IO;
using System.IO.Ports;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Detection;

public sealed record DmdProbeResult(
    bool DmdDeviceDllFound,
    bool ZeDmdDllFound,
    bool DmdExtFound,
    IReadOnlyList<string> SerialPorts,
    IReadOnlyDictionary<string, string> KnownPorts,
    string ToolsFolder)
{
    /// <summary>Serial ports NOT claimed by another device (DMD candidates).</summary>
    public IReadOnlyList<string> CandidatePorts
        => SerialPorts.Where(p => !KnownPorts.ContainsKey(p)).ToList();

    public string Describe()
    {
        var lines = new List<string>
        {
            DmdDeviceDllFound ? L.T("DmdDevice.dll : présent", "DmdDevice.dll: present") : L.T("DmdDevice.dll : introuvable", "DmdDevice.dll: not found"),
            ZeDmdDllFound ? L.T("Librairie ZeDMD : présente", "ZeDMD library: present") : L.T("Librairie ZeDMD : introuvable", "ZeDMD library: not found"),
            DmdExtFound ? L.T("dmdext.exe : présent", "dmdext.exe: present") : L.T("dmdext.exe : introuvable", "dmdext.exe: not found")
        };

        if (SerialPorts.Count == 0)
        {
            lines.Add(L.T("Ports série : aucun détecté", "Serial ports: none detected"));
        }
        else
        {
            var described = SerialPorts.Select(port => KnownPorts.TryGetValue(port, out var owner)
                ? $"{port} = {owner}"
                : port);
            lines.Add(L.T("Ports série : ", "Serial ports: ") + string.Join(", ", described));
            if (CandidatePorts.Count == 0)
            {
                lines.Add(L.T("Aucun DMD série détecté : tous les ports sont attribués à d'autres périphériques.",
                    "No serial DMD detected: every port belongs to another device."));
            }
        }
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Static inventory of the private DMD stack (tools\dmd) and the serial ports. The
/// probe never opens the DMD itself: pushing frames stays an explicit user action,
/// so an unplugged or powered-off panel never blocks the setup. Ports claimed by
/// the sibling LedManager plugin (LedManager.ini [Serial:*] Port=COMx) are labeled
/// as LED panels WITHOUT opening them — a COM5 that is really the button panel no
/// longer reads as a DMD candidate.
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
            KnownPorts: ReadLedManagerPorts(pluginRoot),
            ToolsFolder: tools);
    }

    /// <summary>COM ports the LedManager plugin declares for its Pico senders —
    /// read from its ini, never by opening the port (a single process owns a COM).</summary>
    private static IReadOnlyDictionary<string, string> ReadLedManagerPorts(string pluginRoot)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var ini = Path.GetFullPath(Path.Combine(pluginRoot, "..", "LedManager", "LedManager.ini"));
            if (!File.Exists(ini)) return known;

            string? section = null;
            foreach (var raw in File.ReadLines(ini))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    section = line.Trim('[', ']');
                    continue;
                }
                if (section == null || !section.StartsWith("Serial", StringComparison.OrdinalIgnoreCase)) continue;

                var eq = line.IndexOf('=');
                if (eq <= 0 || !line[..eq].Trim().Equals("Port", StringComparison.OrdinalIgnoreCase)) continue;
                var port = line[(eq + 1)..].Split(';', '#')[0].Trim();
                if (port.Length > 0)
                {
                    var sender = section.Contains(':') ? section[(section.IndexOf(':') + 1)..] : section;
                    known[port] = L.T($"panneau LED (LedManager {sender})", $"LED panel (LedManager {sender})");
                }
            }
        }
        catch
        {
            // unreadable sibling ini: ports stay unlabeled
        }
        return known;
    }
}
