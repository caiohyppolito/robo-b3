using System.Text.Json.Serialization;
using Microsoft.AspNetCore.RateLimiting;
using RoboB3.Api.Auth;
using RoboB3.Api.Endpoints;
using RoboB3.Api.Infrastructure;
using RoboB3.Application;
using RoboB3.Infrastructure;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Security;

if (args is ["hash-password", ..])
{
    return PasswordCommand.Run();
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<RoboB3DbContext>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter(AuthEndpoints.LoginRateLimitPolicy, limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().WithMethods("GET", "POST", "PUT")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var applyMigrations = app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", defaultValue: true);
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(applyMigrations, CancellationToken.None);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapHealthChecks("/health").AllowAnonymous();
app.MapAuthEndpoints();
app.MapTradingEndpoints();
app.MapMarketDataEndpoints();
app.MapBacktestEndpoints();

await app.RunAsync();
return 0;

internal static class PasswordCommand
{
    public static int Run()
    {
        Console.Write("Nova senha do painel: ");
        var password = ReadHidden();
        Console.Write("Repita a senha: ");
        if (password != ReadHidden())
        {
            Console.Error.WriteLine("As senhas não conferem.");
            return 1;
        }

        if (password.Length < 12)
        {
            Console.Error.WriteLine("Use pelo menos 12 caracteres.");
            return 1;
        }

        Console.WriteLine("Hash gerado. Grave com:");
        Console.WriteLine($"  dotnet user-secrets set \"Auth:AdminPasswordHash\" \"{PasswordHasher.Hash(password)}\" --project src/RoboB3.Api");
        return 0;
    }

    private static string ReadHidden()
    {
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace && buffer.Length > 0)
            {
                buffer.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}

/// <summary>Exposto para testes de integração.</summary>
public partial class Program;
