namespace RoboB3.Api.Infrastructure;

internal static class HttpContextExtensions
{
    public static string Actor(this HttpContext context) => context.User.Identity?.Name ?? "desconhecido";
}
