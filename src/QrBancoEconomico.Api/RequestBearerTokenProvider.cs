using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using QrBancoEconomico.Application;
using QrBancoEconomico.Infrastructure;

namespace QrBancoEconomico.Api;

/// <summary>
/// Resuelve la credencial que viaja hacia Baneco, en este orden:
/// <list type="number">
/// <item>token vigente de la cuenta del inventario, obtenido y renovado por el proxy con las credenciales cifradas en la base;</item>
/// <item>token estático de <c>Baneco:Accounts:{credentialRef}</c>, respaldo del mecanismo anterior;</item>
/// <item><c>Authorization: Bearer</c> del llamante (el suscriptor aporta su cuenta: modo pass-through);</item>
/// <item><c>Baneco:BearerToken</c>, respaldo común heredado.</item>
/// </list>
/// La cuenta del inventario gana sobre la cabecera del llamante: un suscriptor no puede sustituir la
/// credencial de la cuenta que el proxy le concedió.
/// </summary>
public sealed class RequestBearerTokenProvider(
    IHttpContextAccessor httpContextAccessor,
    IBanecoAccountContext accountContext,
    IBanecoTokenService tokenService,
    IOptions<BanecoOptions> options,
    ILogger<RequestBearerTokenProvider> logger) : IBanecoTokenProvider
{
    private readonly BanecoOptions _options = options.Value;

    public async Task<string?> GetBearerTokenAsync(CancellationToken ct)
    {
        var account = accountContext.Current;
        if (account is not null)
        {
            var managed = await tokenService.GetTokenAsync(account.Id, ct);
            if (!string.IsNullOrWhiteSpace(managed)) return managed;

            var staticToken = _options.FindCredentials(account.CredentialRef)?.BearerToken;
            if (!string.IsNullOrWhiteSpace(staticToken))
            {
                logger.LogWarning("La cuenta {AccountCode} usa el token estático de configuración: vence a los 30 minutos " +
                                  "y no se renueva solo. Cargue usuario y contraseña en el inventario.", account.Code);
                return staticToken;
            }
        }

        var callerToken = CallerBearerToken();
        if (!string.IsNullOrWhiteSpace(callerToken)) return callerToken;

        return string.IsNullOrWhiteSpace(_options.BearerToken) ? null : _options.BearerToken;
    }

    /// <summary>
    /// Solo hay algo que renovar cuando el token lo gestiona el proxy. Con un token estático o con el
    /// del llamante, reintentar daría exactamente el mismo 401.
    /// </summary>
    public async Task<bool> TryRenewAsync(CancellationToken ct)
    {
        var account = accountContext.Current;
        if (account is null) return false;

        tokenService.Invalidate(account.Id);
        var renewed = await tokenService.GetTokenAsync(account.Id, ct);
        if (string.IsNullOrWhiteSpace(renewed)) return false;

        logger.LogInformation("Baneco respondió 401 para la cuenta {AccountCode}; token renovado y solicitud reintentada.",
            account.Code);
        return true;
    }

    private string? CallerBearerToken()
    {
        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        return AuthenticationHeaderValue.TryParse(authorization, out var header) &&
               string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(header.Parameter)
            ? header.Parameter
            : null;
    }
}
