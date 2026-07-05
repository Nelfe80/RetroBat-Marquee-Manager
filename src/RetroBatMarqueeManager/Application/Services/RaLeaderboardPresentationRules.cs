namespace RetroBatMarqueeManager.Application.Services;

public static class RaLeaderboardPresentationRules
{
    public static bool IsResultEvent(string type, string state)
        => type.EndsWith("submit.confirmed", StringComparison.OrdinalIgnoreCase) ||
           state.Equals("submitted", StringComparison.OrdinalIgnoreCase) ||
           // "submitting" carries the exact final time from the RetroArch log — it is
           // the record-sent moment, so show the result there (the proxy round-trip
           // submit.confirmed may never arrive when RA talks to the server directly)
           state.Equals("submitting", StringComparison.OrdinalIgnoreCase) ||
           state.Equals("completed", StringComparison.OrdinalIgnoreCase);

    public static bool IsSpeedrunClockTimer(string kind, string role, string sourceKey)
        => kind.Contains("leaderboard", StringComparison.OrdinalIgnoreCase) ||
           sourceKey.Contains("leaderboard", StringComparison.OrdinalIgnoreCase) ||
           role.Equals("level", StringComparison.OrdinalIgnoreCase);

    public static double? TimerSeconds(long value, string unit)
    {
        if (unit.Equals("milliseconds", StringComparison.OrdinalIgnoreCase) || unit.Equals("ms", StringComparison.OrdinalIgnoreCase))
            return value / 1000d;
        if (unit.Equals("seconds", StringComparison.OrdinalIgnoreCase) ||
            unit.Equals("second", StringComparison.OrdinalIgnoreCase) ||
            unit.Equals("s", StringComparison.OrdinalIgnoreCase))
            return value;
        if (unit.Equals("minutes", StringComparison.OrdinalIgnoreCase) ||
            unit.Equals("minute", StringComparison.OrdinalIgnoreCase) ||
            unit.Equals("min", StringComparison.OrdinalIgnoreCase))
            return value * 60d;
        return null;
    }
}
