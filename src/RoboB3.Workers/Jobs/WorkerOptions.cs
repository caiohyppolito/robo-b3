using System.ComponentModel.DataAnnotations;

namespace RoboB3.Workers.Jobs;

public sealed class WorkerOptions
{
    public const string SectionName = "Workers";

    [Range(1, 1440)]
    public int MarketDataIntervalMinutes { get; set; } = 60;

    [Range(1, 3600)]
    public int RiskMonitorIntervalSeconds { get; set; } = 15;

    [Range(1, 168)]
    public int LearningIntervalHours { get; set; } = 24;
}
