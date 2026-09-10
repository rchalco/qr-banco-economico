using System.Security.Claims;
using QrBancoEconomico.Application;

namespace QrBancoEconomico.Api.Security;

/// <summary>
/// Expone el suscriptor autenticado a las capas superiores. Solo debe resolverse dentro de endpoints
/// protegidos: en un endpoint anónimo no hay identidad y la lectura falla de forma explícita.
/// </summary>
public sealed class HttpSubscriberContext(IHttpContextAccessor httpContextAccessor) : ISubscriberContext
{
    public Guid SubscriberId =>
        Guid.TryParse(Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("La solicitud no tiene un suscriptor autenticado.");

    public string Code => Principal.FindFirstValue(ClaimTypes.Name)
        ?? throw new InvalidOperationException("La solicitud no tiene un suscriptor autenticado.");

    public IReadOnlySet<string> Scopes => Principal
        .FindAll(ApiKeyAuthenticationDefaults.ScopeClaimType)
        .Select(c => c.Value)
        .ToHashSet(StringComparer.Ordinal);

    private ClaimsPrincipal Principal => httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No hay una solicitud HTTP en curso.");
}
