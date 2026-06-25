namespace ApproveFlow.MVC.Models.Domain;

public static class IstClock
{
    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    public static DateTimeOffset Now =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);

    public static int DaysSince(DateTimeOffset past)
    {
        var todayIst = DateOnly.FromDateTime(Now.DateTime);
        var pastIst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(past, Ist).DateTime);
        return Math.Max(0, todayIst.DayNumber - pastIst.DayNumber);
    }
}
