namespace CallingBell.Application.Common;

/// <summary>
/// The platform operates in India; "open now", slots and daily reports use IST (UTC+05:30).
/// </summary>
public static class IndianTime
{
    public static readonly TimeSpan Offset = new(5, 30, 0);

    public static DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(Offset);

    public static DateTime Today => Now.Date;

    public static DateTimeOffset At(DateTime date, TimeSpan time) => new(date.Date + time, Offset);
}
