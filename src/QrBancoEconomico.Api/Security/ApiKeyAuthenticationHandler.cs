using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using QrBancoEconomico.Application;
using QrBancoEconomico.Infrastructure;

namespace QrBancoEconomico.Api.Security;

public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";
    public const string ScopeClaimType = "scope";
    public const string ApiKeyIdClaimType = "apikey_id";
    public const string RateLimitClaimType = "requests_per_minute";
}

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IApiKeyStore store,
    IOptions<ApiKeySecurityOptions> keyOptions) : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    private const string FailureDetailKey = "ApiKey.FailureDetail";

    private readonly ApiKeySecurityOptions _keyOptions = keyOptions.Value;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers[_keyOptions.HeaderName];
        if (StringValues.IsNullOrEmpty(header)) return AuthenticateResult.NoResult();
        if (header.Count > 1) return AuthenticateResult.Fail("Se envió más de una API Key.");

        var presentedKey = header.ToString();
        var result = await store.ValidateAsync(presentedKey, Context.RequestAborted);
        if (result.Subscriber is null)
        {
            // La clave presentada NO se registra bajo ningún concepto: ahora es el secreto completo, no
            // un identificador público. Un log con la clave rechazada sería un log con una credencial.
            Logger.LogWarning("API Key rechazada ({Failure}) desde {RemoteIp}.",
                result.Failure, Context.Connection.RemoteIpAddress?.ToString() ?? "desconocida");
            var detail = DescribeFailure(result.Failure);
            Context.Items[FailureDetailKey] = detail;
            return AuthenticateResult.Fail(detail);
        }

        var subscriber = result.Subscriber;
        await store.RegisterUseAsync(subscriber.ApiKeyId, Context.RequestAborted);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subscriber.SubscriberId.ToString()),
            new(ClaimTypes.Name, subscriber.Code),
            new(ApiKeyAuthenticationDefaults.ApiKeyIdClaimType, subscriber.ApiKeyId.ToString()),
            new(ApiKeyAuthenticationDefaults.RateLimitClaimType, subscriber.RequestsPerMinute.ToString())
        };
        claims.AddRange(subscriber.Scopes.Select(scope => new Claim(ApiKeyAuthenticationDefaults.ScopeClaimType, scope)));

        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.Scheme, ClaimTypes.Name, ClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = $"{ApiKeyAuthenticationDefaults.Scheme} header=\"{_keyOptions.HeaderName}\"";
        var detail = Context.Items.TryGetValue(FailureDetailKey, out var value) && value is string message
            ? message
            : $"Envíe una API Key válida en la cabecera {_keyOptions.HeaderName}.";
        return WriteProblemAsync(StatusCodes.Status401Unauthorized, "No autenticado", detail);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteProblemAsync(StatusCodes.Status403Forbidden, "Alcance insuficiente",
            "La API Key es válida pero no tiene el alcance requerido por este recurso.");

    private async Task WriteProblemAsync(int status, string title, string detail)
    {
        Response.StatusCode = status;
        Response.ContentType = "application/problem+json";
        await Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = $"https://httpstatuses.io/{status}",
            title,
            status,
            detail,
            traceId = Context.TraceIdentifier
        }), Context.RequestAborted);
    }

    /// <summary>Mensajes deliberadamente genéricos: no revelan si la clave existe ni por qué falló.</summary>
    private static string DescribeFailure(ApiKeyFailure failure) => failure switch
    {
        ApiKeyFailure.Expired => "API Key expirada.",
        ApiKeyFailure.Revoked => "API Key revocada.",
        ApiKeyFailure.SubscriberDisabled => "Suscriptor inhabilitado.",
        _ => "API Key inválida."
    };
}
