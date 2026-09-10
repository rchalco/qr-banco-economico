using Microsoft.EntityFrameworkCore;
using QrBancoEconomico.Application;

namespace QrBancoEconomico.Infrastructure;

/// <summary>
/// Decide qué cuenta de Baneco atiende cada solicitud, a partir del inventario N-N.
/// Un suscriptor sin cuentas concedidas queda en modo pass-through: aporta sus propias credenciales
/// de banco, que es el comportamiento previo al inventario.
/// </summary>
public sealed class BanecoAccountResolver(BanecoDbContext db, ISecretProtector protector) : IBanecoAccountResolver
{
    public async Task<BanecoAccountResolution> ResolveAsync(Guid subscriberId, string? requestedAccountCode, CancellationToken ct)
    {
        var grants = await GrantsOf(subscriberId).ToListAsync(ct);
        if (grants.Count == 0) return BanecoAccountResolution.Rejected(BanecoAccountResolutionStatus.PassThrough);

        var requested = requestedAccountCode?.Trim();
        var grant = string.IsNullOrEmpty(requested)
            ? grants.Count == 1 ? grants[0] : grants.SingleOrDefault(g => g.IsDefault)
            : grants.SingleOrDefault(g => string.Equals(g.Code, requested, StringComparison.OrdinalIgnoreCase));

        if (grant is null)
            return BanecoAccountResolution.Rejected(string.IsNullOrEmpty(requested)
                ? BanecoAccountResolutionStatus.Ambiguous
                : BanecoAccountResolutionStatus.NotGranted);

        if (!grant.IsActive) return BanecoAccountResolution.Rejected(BanecoAccountResolutionStatus.Inactive);

        return BanecoAccountResolution.Resolved(
            new ResolvedBanecoAccount(grant.AccountId, grant.Code, grant.Name, grant.CredentialRef));
    }

    public async Task<IReadOnlyList<GrantedAccountSummary>> ListGrantedAsync(Guid subscriberId, CancellationToken ct)
    {
        var grants = await GrantsOf(subscriberId).ToListAsync(ct);
        return grants
            .Select(g => new GrantedAccountSummary(g.AccountId, g.Code, g.Name, g.IsDefault, g.IsActive, g.GrantedAt, g.GrantedBy))
            .OrderBy(g => g.Code, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<string?> ResolveAccountCreditAsync(Guid subscriberId, CancellationToken ct)
    {
        var stored = await db.Subscribers.AsNoTracking()
            .Where(x => x.Id == subscriberId)
            .Select(x => x.SavingsAccountEncrypted)
            .SingleOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(stored) ? null : protector.Unprotect(stored);
    }

    private IQueryable<GrantRow> GrantsOf(Guid subscriberId) => db.SubscriberBanecoAccounts.AsNoTracking()
        .Where(x => x.SubscriberId == subscriberId)
        .Select(x => new GrantRow(x.BanecoAccountId, x.BanecoAccount!.Code, x.BanecoAccount.Name,
            x.BanecoAccount.CredentialRef, x.BanecoAccount.IsActive, x.IsDefault, x.GrantedAt, x.GrantedBy));

    private sealed record GrantRow(Guid AccountId, string Code, string Name, string? CredentialRef,
        bool IsActive, bool IsDefault, DateTimeOffset GrantedAt, string? GrantedBy);
}
