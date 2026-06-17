using System;
using System.Globalization;
using System.Linq;

namespace WebstationBackup.Agent.Service.Core;

internal sealed class ScheduleEvaluator
{
    public (bool ShouldRunNow, string WindowKeyLocal, DateTimeOffset WindowStartUtc, DateTimeOffset WindowEndUtc) Evaluate(ProjectRules rules, DateTimeOffset nowUtc)
        => Evaluate(
            rules.Defaults.Schedule.Enabled,
            rules.Defaults.Schedule.Timezone,
            rules.Defaults.Schedule.DaysOfWeek,
            rules.Defaults.Schedule.StartTimeLocal,
            rules.Defaults.Schedule.MaxRuntimeMinutes,
            nowUtc);

    public (bool ShouldRunNow, string WindowKeyLocal, DateTimeOffset WindowStartUtc, DateTimeOffset WindowEndUtc) Evaluate(
        bool enabled,
        string timezone,
        string[] daysOfWeek,
        string startTimeLocal,
        int maxRuntimeMinutes,
        DateTimeOffset nowUtc)
    {
        if (!enabled)
        {
            return (false, "disabled", DateTimeOffset.MinValue, DateTimeOffset.MinValue);
        }

        var tz = ResolveTimeZone(timezone);
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, tz);

        var localDow = nowLocal.DayOfWeek;
        if (!IsAllowedDay(daysOfWeek, localDow))
        {
            return (false, nowLocal.ToString("yyyyMMdd", CultureInfo.InvariantCulture), DateTimeOffset.MinValue, DateTimeOffset.MinValue);
        }

        var start = ParseLocalTime(startTimeLocal);
        var windowStartLocal = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, start.Hour, start.Minute, 0, DateTimeKind.Unspecified);
        var windowEndLocal = windowStartLocal.AddMinutes(Math.Max(1, maxRuntimeMinutes));

        var windowStartUtc = TimeZoneInfo.ConvertTimeToUtc(windowStartLocal, tz);
        var windowEndUtc = TimeZoneInfo.ConvertTimeToUtc(windowEndLocal, tz);

        var shouldRun = nowUtc >= windowStartUtc && nowUtc <= windowEndUtc;
        var key = nowLocal.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return (shouldRun, key, windowStartUtc, windowEndUtc);
    }

    private static bool IsAllowedDay(string[] configuredDays, DayOfWeek dow)
    {
        if (configuredDays is null || configuredDays.Length == 0)
        {
            return true;
        }

        var token = dow switch
        {
            DayOfWeek.Monday => "MON",
            DayOfWeek.Tuesday => "TUE",
            DayOfWeek.Wednesday => "WED",
            DayOfWeek.Thursday => "THU",
            DayOfWeek.Friday => "FRI",
            DayOfWeek.Saturday => "SAT",
            DayOfWeek.Sunday => "SUN",
            _ => "UNK"
        };

        return configuredDays.Any(d => string.Equals(d, token, StringComparison.OrdinalIgnoreCase));
    }

    private static (int Hour, int Minute) ParseLocalTime(string startTimeLocal)
    {
        if (string.IsNullOrWhiteSpace(startTimeLocal))
        {
            return (22, 0);
        }

        var parts = startTimeLocal.Split(':');
        if (parts.Length != 2)
        {
            return (22, 0);
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)) h = 22;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)) m = 0;
        h = Math.Max(0, Math.Min(23, h));
        m = Math.Max(0, Math.Min(59, m));
        return (h, m);
    }

    private static TimeZoneInfo ResolveTimeZone(string tzId)
    {
        if (!string.IsNullOrWhiteSpace(tzId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(tzId);
            }
            catch
            {
            }
        }

        return TimeZoneInfo.Local;
    }
}
