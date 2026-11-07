using Microsoft.Extensions.DependencyInjection;
using RoboB3.Application.Backtesting;
using RoboB3.Application.Execution;
using RoboB3.Application.Learning;
using RoboB3.Application.MarketData;
using RoboB3.Application.Paper;
using RoboB3.Application.Risk;

namespace RoboB3.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<MarketDataIngestionService>();
        services.AddScoped<KillSwitchService>();
        services.AddScoped<RiskSettingsService>();
        services.AddScoped<RiskMonitorService>();
        services.AddScoped<BacktestService>();
        services.AddScoped<LearningService>();
        services.AddScoped<PaperTradingService>();
        services.AddScoped<IntradayIngestionService>();
        services.AddScoped<StrategyAssignmentService>();
        services.AddScoped<ExecutionService>();
        services.AddSingleton<SimulatedOrderExecutor>();
        services.AddScoped<StrategyComparisonService>();
        return services;
    }
}
