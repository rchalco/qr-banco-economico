using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QrBancoEconomico.Application;
using QrBancoEconomico.Infrastructure;

namespace QrBancoEconomico.Api.Security;

public static class ApiKeySecurityExtensions
{
    public static IServiceCollection AddApiKeySecurity(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<ApiKeySecurityOptions>(configuration.GetSection(ApiKeySecurityOptions.SectionName));
        services.PostConfigure<ApiKeySecurityOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.Environment))
                options.Environment = ApiKeyMaterial.EnvironmentTag(environment.EnvironmentName);
            options.Environment = options.Environment.Trim().ToLowerInvariant();
        });

        services.AddMemoryCache();
        services.AddScoped<IApiKeyStore, ApiKeyStore>();
        services.AddScoped<ISubscriberProvisioning, SubscriberProvisioning>();
        services.AddScoped<ISubscriberContext, HttpSubscriberContext>();
        services.AddScoped<IBanecoAccountResolver, BanecoAccountResolver>();
        services.AddScoped<IBanecoAccountInventory, BanecoAccountInventory>();
        services.AddSingleton<IBanecoAccountContext, HttpBanecoAccountContext>();

        services.AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationDefaults.Scheme, null);

        services.AddAuthorizationBuilder()
            // Cierre por defecto: todo endpoint exige credencial salvo que se marque [AllowAnonymous].
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(ApiKeyAuthenticationDefaults.Scheme)
                .RequireAuthenticatedUser()
                .Build())
            .AddScopePolicies();

        return services;
    }

    private static AuthorizationBuilder AddScopePolicies(this AuthorizationBuilder builder)
    {
        foreach (var scope in ApiScopes.All)
        {
            builder.AddPolicy(scope, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(ApiKeyAuthenticationDefaults.ScopeClaimType, scope));
        }

        return builder;
    }

    /// <summary>
    /// Cuota por suscriptor en ventana fija de un minuto. El tráfico sin credencial se particiona por IP
    /// para que un origen abusivo no consuma la cuota de los demás.
    /// </summary>
    public static IServiceCollection AddSubscriberRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var keyOptions = context.RequestServices
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiKeySecurityOptions>>().Value;

                var subscriberCode = context.User.FindFirstValue(ClaimTypes.Name);
                if (subscriberCode is null)
                {
                    var origin = context.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
                    return RateLimitPartition.GetFixedWindowLimiter($"ip:{origin}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = keyOptions.AnonymousRequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1)
                    });
                }

                var permitLimit = int.TryParse(context.User.FindFirstValue(ApiKeyAuthenticationDefaults.RateLimitClaimType),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var rpm) && rpm > 0
                    ? rpm
                    : keyOptions.AnonymousRequestsPerMinute;

                return RateLimitPartition.GetFixedWindowLimiter($"sub:{subscriberCode}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1)
                });
            });

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);

                await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    type = "https://httpstatuses.io/429",
                    title = "Cuota excedida",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "Se superó el límite de solicitudes por minuto asignado al suscriptor.",
                    traceId = context.HttpContext.TraceIdentifier
                }), ct);
            };
        });

        return services;
    }

    /// <summary>
    /// Emite una clave `admin` inicial cuando la base no tiene ninguna clave activa y el arranque lo habilita.
    /// Se imprime una única vez por salida estándar (no al archivo de log) y no puede recuperarse después.
    /// </summary>
    public static async Task EnsureBootstrapApiKeyAsync(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiKeySecurityOptions>>().Value;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ApiKeys.Bootstrap");

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BanecoDbContext>();

        try
        {
            if (await db.SubscriberApiKeys.AnyAsync(x => x.RevokedAt == null)) return;

            if (!options.BootstrapAdminKey)
            {
                logger.LogCritical("No existe ninguna API Key activa: todas las solicitudes serán rechazadas con 401. " +
                                   "Habilite ApiKeys__BootstrapAdminKey=true una sola vez para emitir la clave administrativa inicial.");
                return;
            }

            var provisioning = scope.ServiceProvider.GetRequiredService<ISubscriberProvisioning>();
            var subscriberId = await db.Subscribers
                .Where(x => x.Code == BootstrapSubscriberCode)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync();

            if (subscriberId is null)
            {
                var created = await provisioning.CreateSubscriberAsync(
                    // Sin cuenta de ahorro a propósito: el suscriptor de bootstrap solo administra, no emite QR.
                    new CreateSubscriberRequest(BootstrapSubscriberCode, "Administración de suscriptores", null, null, 60),
                    CancellationToken.None);
                if (created is null)
                {
                    logger.LogCritical("Otro proceso creó el suscriptor {Code} durante el arranque; no se emitió la clave inicial.",
                        BootstrapSubscriberCode);
                    return;
                }

                subscriberId = created.Id;
            }

            var issued = await provisioning.IssueApiKeyAsync(subscriberId.Value,
                new IssueApiKeyRequest([ApiScopes.Admin], "bootstrap", null), CancellationToken.None);
            if (issued is null)
            {
                logger.LogCritical("No se pudo emitir la API Key administrativa inicial: suscriptor {Code} inexistente.",
                    BootstrapSubscriberCode);
                return;
            }

            PrintBootstrapKey(issued.ApiKey.ToString());
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "No se pudo verificar ni emitir la API Key administrativa inicial.");
        }
    }

    private const string BootstrapSubscriberCode = "bootstrap-admin";

    private static void PrintBootstrapKey(string apiKey)
    {
        Console.WriteLine();
        Console.WriteLine("== API Key administrativa inicial (se muestra una sola vez) ==");
        Console.WriteLine(apiKey);
        Console.WriteLine("Guárdela en la bóveda de secretos, deshabilite ApiKeys__BootstrapAdminKey y rótela cuanto antes.");
        Console.WriteLine("También queda legible en SubscriberApiKeys.ApiKey: restrinja el acceso a esa tabla.");
        Console.WriteLine();
    }
}
