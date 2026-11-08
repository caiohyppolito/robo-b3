using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Backtesting;
using RoboB3.Application.Learning;
using RoboB3.Application.Execution;
using RoboB3.Application.Paper;
using RoboB3.Infrastructure.Execution;
using RoboB3.Infrastructure.MarketData.Mt5;
using RoboB3.Infrastructure.Learning;
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
        services.AddScoped<IBacktestRunRepository, BacktestRunRepository>();
        services.AddScoped<IMetaModelRepository, MetaModelRepository>();
        services.AddScoped<ITradeJournalRepository, TradeJournalRepository>();
        services.AddSingleton<IMetaModelEngine, MlNetMetaModelEngine>();
        services.AddScoped<IModelAnalyst, ClaudeModelAnalyst>();
        services.AddOptions<AnthropicOptions>().Bind(configuration.GetSection(AnthropicOptions.SectionName));
        services.AddOptions<LearningOptions>()
            .Bind(configuration.GetSection(LearningOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<DatabaseInitializer>();

        services.AddOptions<TradingCapitalOptions>()
            .Bind(configuration.GetSection(TradingCapitalOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<PaperTradingOptions>()
            .Bind(configuration.GetSection(PaperTradingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IPortfolioProvider, PaperPortfolioProvider>();
        services.AddScoped<IPaperRepository, PaperRepository>();
        services.AddScoped<IStrategyAssignmentRepository, StrategyAssignmentRepository>();
        services.AddScoped<IExecutionSettingsRepository, ExecutionSettingsRepository>();
        services.AddScoped<IOrderExecutorFactory, OrderExecutorFactory>();
        services.AddScoped<IOrderIntentRepository, OrderIntentRepository>();
        services.AddScoped<IExperimentRepository, ExperimentRepository>();
        services.AddScoped<Application.MarketData.ICdiRepository, CdiRepository>();
        services.AddScoped<ITradingLock, SqlServerTradingLock>();
        services.AddOptions<ExecutionOptions>().Bind(configuration.GetSection(ExecutionOptions.SectionName));

        // Cliente de ordens SEM novas tentativas automáticas: repetir uma ordem pode duplicá-la.
        services.AddHttpClient(OrderExecutorFactory.HttpClientName, (sp, client) =>
        {
            var gateway = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GatewayOptions>>().Value;
            client.BaseAddress = gateway.BaseUrl;
            client.Timeout = TimeSpan.FromSeconds(20);
            if (!string.IsNullOrWhiteSpace(gateway.Token))
            {
                client.DefaultRequestHeaders.Add(Mt5GatewayFeed.TokenHeader, gateway.Token);
            }
        });

        services.AddOptions<GatewayOptions>()
            .Bind(configuration.GetSection(GatewayOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<IMarketDataFeed, Mt5GatewayFeed>((sp, client) =>
            {
                var gateway = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GatewayOptions>>().Value;
                client.BaseAddress = gateway.BaseUrl;
                client.Timeout = TimeSpan.FromSeconds(30);
                if (!string.IsNullOrWhiteSpace(gateway.Token))
                {
                    client.DefaultRequestHeaders.Add(Mt5GatewayFeed.TokenHeader, gateway.Token);
                }
            })
            .AddStandardResilienceHandler();
        services.AddHttpClient<Application.MarketData.ICdiProvider, MarketData.Bcb.BcbCdiProvider>(client =>
            {
                client.BaseAddress = new Uri("https://api.bcb.gov.br/");
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .AddStandardResilienceHandler();

        services.AddOptions<Application.Notifications.TelegramOptions>()
            .Bind(configuration.GetSection(Application.Notifications.TelegramOptions.SectionName));

        // Sem logs de requisição (a URL carrega o token) e sem retry automático (evita mensagens e comandos duplicados).
        services.AddHttpClient<Application.Notifications.ITelegramClient, Notifications.TelegramBotClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.telegram.org/");
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .RemoveAllLoggers();

        services.AddOptions<BacktestOptions>()
            .Bind(configuration.GetSection(BacktestOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

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
