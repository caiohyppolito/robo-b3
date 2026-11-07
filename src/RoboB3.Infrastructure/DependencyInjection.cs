using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoboB3.Application.Abstractions;
using RoboB3.Infrastructure.MarketData;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Persistence.Repositories;
using RoboB3.Infrastructure.Portfolio;

namespace RoboB3.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "RoboB3";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' não configurada. Veja o README.");

        services.AddDbContext<RoboB3DbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<RoboB3DbContext>());
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<ICandleRepository, CandleRepository>();
        services.AddScoped<IRiskSettingsRepository, RiskSettingsRepository>();
        services.AddScoped<ITradingStateRepository, TradingStateRepository>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<DatabaseInitializer>();

        services.AddOptions<TradingOptions>()
            .Bind(configuration.GetSection(TradingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IPortfolioProvider, SimulatedPortfolioProvider>();

        services.AddHttpClient<IMarketDataProvider, YahooFinanceMarketDataProvider>(client =>
            {
                client.BaseAddress = new Uri("https://query1.finance.yahoo.com/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; robo-b3/1.0)");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddStandardResilienceHandler();

        return services;
    }
}
