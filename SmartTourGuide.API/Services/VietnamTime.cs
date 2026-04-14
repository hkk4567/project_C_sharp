namespace SmartTourGuide.API.Services;

public static class VietnamTime
{
    private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VietnamTimeZone);

    public static DateTime ToVietnamTime(DateTime value)
    {
        if (value == default) return default;

        if (value.Kind == DateTimeKind.Utc)
            return TimeZoneInfo.ConvertTimeFromUtc(value, VietnamTimeZone);

        if (value.Kind == DateTimeKind.Local)
            return TimeZoneInfo.ConvertTime(value, VietnamTimeZone);

        // Unspecified: giữ nguyên và xem như thời gian local đã được client chuẩn hóa.
        return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
    }
}
