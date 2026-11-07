using Microsoft.Extensions.Options;
using RoboB3.Api.Auth;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Audit;
using RoboB3.Infrastructure.Security;

namespace RoboB3.Api.Endpoints;

internal static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithTags("Auth");
        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IOptions<AuthOptions> options,
        TokenService tokens,
        IAuditLog audit,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        // Compara usuário e senha sempre, para não revelar qual dos dois está errado pelo tempo de resposta.
        var userMatches = string.Equals(request.Username, settings.AdminUsername, StringComparison.Ordinal);
        var passwordMatches = PasswordHasher.Verify(request.Password, settings.AdminPasswordHash);
        var success = userMatches && passwordMatches;

        var actor = string.IsNullOrWhiteSpace(request.Username) ? "anônimo" : request.Username[..Math.Min(100, request.Username.Length)];
        audit.Record(new AuditEntry(success ? "auth.login" : "auth.login-failed", actor, null, clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!success)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Usuário ou senha inválidos");
        }

        var token = tokens.Issue(settings.AdminUsername);
        return Results.Ok(new LoginResponse(token.Token, token.ExpiresAt));
    }
}
