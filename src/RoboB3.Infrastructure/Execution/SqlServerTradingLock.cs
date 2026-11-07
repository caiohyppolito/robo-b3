using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Abstractions;
using RoboB3.Infrastructure.Persistence;

namespace RoboB3.Infrastructure.Execution;

/// <summary>
/// Trava exclusiva do ciclo de operação no próprio SQL Server (sp_getapplock), válida entre processos:
/// o worker e a API nunca rodam dois ciclos ao mesmo tempo. A trava cai sozinha se o processo morrer,
/// porque pertence à sessão (conexão) que a obteve.
/// </summary>
internal sealed class SqlServerTradingLock(RoboB3DbContext db) : ITradingLock
{
    private const string Resource = "robo-b3:ciclo-de-operacao";

    // Provedores sem SQL Server (testes em memória): trava do processo.
    private static readonly SemaphoreSlim LocalLock = new(1, 1);

    public async Task<IAsyncDisposable?> TryAcquireAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer())
        {
            return await LocalLock.WaitAsync(wait, cancellationToken) ? new Release(() => { LocalLock.Release(); return ValueTask.CompletedTask; }) : null;
        }

        var connection = new SqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @timeout; SELECT @r;";
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = Resource });
        command.Parameters.Add(new SqlParameter("@timeout", SqlDbType.Int) { Value = (int)wait.TotalMilliseconds });
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0)
        {
            await connection.DisposeAsync();
            return null;
        }

        return new Release(async () =>
        {
            await using (var release = connection.CreateCommand())
            {
                release.CommandText = "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';";
                release.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = Resource });
                await release.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await connection.DisposeAsync();
        });
    }

    private sealed class Release(Func<ValueTask> release) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _released, 1) == 0 ? release() : ValueTask.CompletedTask;
    }
}
