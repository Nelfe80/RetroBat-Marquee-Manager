using System.Diagnostics;

namespace RetroBatMarqueeManager;

/// <summary>
/// Sets the process priority class from a config string. Used at startup (navigation
/// priority) and switched at game start/end: high while browsing ES so the marquee
/// keeps up, lowered during emulation so the game/emulator gets the CPU (input latency).
/// </summary>
public static class ProcessPriorityHelper
{
    public static ProcessPriorityClass Parse(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "normal" => ProcessPriorityClass.Normal,
        "abovenormal" or "above" => ProcessPriorityClass.AboveNormal,
        "high" => ProcessPriorityClass.High,
        _ => ProcessPriorityClass.BelowNormal
    };

    public static void Apply(string? value)
    {
        try { Process.GetCurrentProcess().PriorityClass = Parse(value); }
        catch { /* unprivileged environments keep the default priority */ }
    }
}
