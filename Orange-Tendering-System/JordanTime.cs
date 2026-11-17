namespace Orange_Tendering_System;

/// <summary>
/// The system's display time zone: Jordan (Asia/Amman, UTC+3 all year). Values are stored in UTC in SQL Server;
/// every page, notification and e-mail shows them converted with these helpers. Storage and comparisons stay UTC.
/// </summary>
public static class JordanTime
{
    public const string ZoneId = "Asia/Amman";
    public const string Label = "Jordan time";
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { ZoneId, "Jordan Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        return TimeZoneInfo.CreateCustomTimeZone(ZoneId, TimeSpan.FromHours(3), "Jordan", "Jordan");
    }

    /// <summary>Current time in Jordan.</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>Converts a stored UTC value (Kind Utc or Unspecified) to Jordan time.</summary>
    public static DateTime ToJordan(this DateTime utc) =>
        utc.Kind == DateTimeKind.Local ? TimeZoneInfo.ConvertTime(utc, Zone)
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static string ToJordan(this DateTime utc, string format) => utc.ToJordan().ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    public static DateTime ToJordan(this DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Zone).DateTime;
    public static string ToJordan(this DateTimeOffset value, string format) => value.ToJordan().ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Standard display, e.g. "2026-11-17 14:30 (Jordan time)".</summary>
    public static string Display(DateTime utc) => utc.ToJordan("yyyy-MM-dd HH:mm") + " (" + Label + ")";
}
