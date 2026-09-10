using QrBancoEconomico.Application;

namespace QrBancoEconomico.Api.Security;

/// <summary>
/// Transporta la cuenta de Baneco resuelta para la solicitud en curso, desde el controlador que la
/// resuelve hasta el proveedor de token que la usa. Se apoya en <c>HttpContext.Items</c> para no
/// depender del ciclo de vida de los servicios que la consultan.
/// </summary>
public sealed class HttpBanecoAccountContext(IHttpContextAccessor httpContextAccessor) : IBanecoAccountContext
{
    private const string ItemKey = "Baneco.ResolvedAccount";

    public ResolvedBanecoAccount? Current =>
        httpContextAccessor.HttpContext?.Items.TryGetValue(ItemKey, out var value) == true
            ? value as ResolvedBanecoAccount
            : null;

    public void Set(ResolvedBanecoAccount account)
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No hay una solicitud HTTP en curso.");
        context.Items[ItemKey] = account;
    }
}
