using Microsoft.Extensions.DependencyInjection;
using RoboB3.Application.MarketData;
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
        return services;
    }
}
