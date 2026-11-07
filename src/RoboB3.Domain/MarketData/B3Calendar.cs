namespace RoboB3.Domain.MarketData;

/// <summary>Fuso e datas de pregão da B3 (America/Sao_Paulo, UTC−3, sem horário de verão desde 2019).</summary>
public static class B3Calendar
{
    public static TimeZoneInfo TimeZone { get; } = LoadTimeZone();

    public static DateTimeOffset ToLocal(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, TimeZone);

    /// <summary>Data do pregão a que um instante pertence.</summary>
    public static DateOnly TradingDate(DateTimeOffset time) => DateOnly.FromDateTime(ToLocal(time).DateTime);

    public static TimeOnly LocalTime(DateTimeOffset time) => TimeOnly.FromDateTime(ToLocal(time).DateTime);

    private static TimeZoneInfo LoadTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("B3", TimeSpan.FromHours(-3), "Brasília", "Brasília");
        }
    }
}
