using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RoboB3.Infrastructure.Persistence;

/// <summary>Usado só pelo "dotnet ef" para gerar migrações; não conecta no banco.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RoboB3DbContext>
{
    public RoboB3DbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RoboB3DbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=RoboB3;Trusted_Connection=True")
            .Options;
        return new RoboB3DbContext(options);
    }
}
