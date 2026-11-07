namespace RoboB3.Workers.Jobs;

/// <summary>
/// Executa um job em intervalo fixo, cada execução no seu próprio escopo de DI (DbContext novo).
/// Falhas são registradas e não derrubam o processo.
/// </summary>
public abstract partial class PeriodicScopedWorker(IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService
{
    protected abstract string JobName { get; }

    protected abstract TimeSpan Interval { get; }

    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken stoppingToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(JobName, Interval);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await RunOnceAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogFailed(ex, JobName);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {Job} iniciado, intervalo {Interval}")]
    private partial void LogStarted(string job, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Job} falhou; nova tentativa no próximo ciclo")]
    private partial void LogFailed(Exception exception, string job);
}
