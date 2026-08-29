using System.Net.Http.Headers;
using QrBancoEconomico.Application;

namespace QrBancoEconomico.Api;

public sealed class RequestBearerTokenProvider(IHttpContextAccessor httpContextAccessor) : IBanecoTokenProvider
{
    public string? GetBearerToken()
    {
        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        return AuthenticationHeaderValue.TryParse(authorization, out var header) &&
               string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(header.Parameter)
            ? header.Parameter
            : null;
    }
}
