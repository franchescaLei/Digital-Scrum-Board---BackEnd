namespace DigitalScrumBoard1.Utilities;

public static class DateTimeHelper
{
    private static readonly TimeZoneInfo PhilippineTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhilippineTimeZone);

    public static DateOnly Today => DateOnly.FromDateTime(Now);
}
