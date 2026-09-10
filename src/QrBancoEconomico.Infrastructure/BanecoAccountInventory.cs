using Microsoft.EntityFrameworkCore;
using QrBancoEconomico.Application;
using QrBancoEconomico.Domain;

namespace QrBancoEconomico.Infrastructure;

/// <summary>
/// Inventario de cuentas de Baneco y de su concesión a suscriptores. Registra la relación N-N y las
/// credenciales del banco, que se guardan cifradas con la llave maestra y nunca se devuelven por la API:
/// solo salen hacia <see cref="IBanecoTokenService"/> para autenticar.
/// </summary>
public sealed class BanecoAccountInventory(
    BanecoDbContext db,
    ISecretProtector protector,
    IBanecoTokenService tokenService) : IBanecoAccountInventory
{
    public async Task<IReadOnlyList<BanecoAccountSummary>> ListAccountsAsync(CancellationToken ct)
    {
        var accounts = await AccountsWithSubscribers().OrderBy(x => x.Code).ToListAsync(ct);
        return accounts.Select(ToSummary).ToList();
    }

    public async Task<BanecoAccountSummary?> GetAccountAsync(Guid accountId, CancellationToken ct)
    {
        var account = await AccountsWithSubscribers().SingleOrDefaultAsync(x => x.Id == accountId, ct);
        return account is null ? null : ToSummary(account);
    }

    public async Task<BanecoAccountSummary?> CreateAccountAsync(CreateBanecoAccountRequest request, CancellationToken ct)
    {
        if (await db.BanecoAccounts.AnyAsync(x => x.Code == request.Code, ct)) return null;

        var account = new BanecoAccount
        {
            Code = request.Code,
            Name = request.Name,
            CredentialRef = request.CredentialRef,
            AccountCodeEncrypted = request.AccountCodeEncrypted,
            BatchDebitAccountEncrypted = request.BatchDebitAccountEncrypted,
            AuthUserNameEncrypted = ProtectOrNull(request.AuthUserName),
            AuthPasswordEncrypted = ProtectOrNull(request.AuthPassword)
        };
        db.BanecoAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return ToSummary(account);
    }

    public async Task<BanecoAccountSummary?> UpdateAccountAsync(Guid accountId, UpdateBanecoAccountRequest request, CancellationToken ct)
    {
        var account = await db.BanecoAccounts.Include(x => x.Subscribers).ThenInclude(x => x.Subscriber)
            .SingleOrDefaultAsync(x => x.Id == accountId, ct);
        if (account is null) return null;

        if (request.Name is not null) account.Name = request.Name;
        if (request.CredentialRef is not null) account.CredentialRef = request.CredentialRef;
        if (request.AccountCodeEncrypted is not null) account.AccountCodeEncrypted = request.AccountCodeEncrypted;
        if (request.BatchDebitAccountEncrypted is not null) account.BatchDebitAccountEncrypted = request.BatchDebitAccountEncrypted;
        if (request.IsActive is not null) account.IsActive = request.IsActive.Value;

        var credentialsChanged = request.AuthUserName is not null || request.AuthPassword is not null;
        if (request.AuthUserName is not null) account.AuthUserNameEncrypted = ProtectOrNull(request.AuthUserName);
        if (request.AuthPassword is not null) account.AuthPasswordEncrypted = ProtectOrNull(request.AuthPassword);
        if (credentialsChanged) account.CredentialsVerifiedAt = null;

        await db.SaveChangesAsync(ct);
        // El token cacheado se emitió con las credenciales anteriores: se descarta para que la próxima
        // solicitud autentique de nuevo en lugar de fallar con 401 hasta que venza.
        if (credentialsChanged) tokenService.Invalidate(accountId);
        return ToSummary(account);
    }

    public async Task<GrantResult> GrantAsync(Guid subscriberId, GrantAccountRequest request, string? grantedBy, CancellationToken ct)
    {
        if (!await db.Subscribers.AnyAsync(x => x.Id == subscriberId, ct))
            return new GrantResult(GrantStatus.SubscriberNotFound, null);

        var account = await db.BanecoAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.AccountId, ct);
        if (account is null) return new GrantResult(GrantStatus.AccountNotFound, null);

        var grant = await db.SubscriberBanecoAccounts
            .SingleOrDefaultAsync(x => x.SubscriberId == subscriberId && x.BanecoAccountId == request.AccountId, ct);
        if (grant is null)
        {
            grant = new SubscriberBanecoAccount { SubscriberId = subscriberId, BanecoAccountId = request.AccountId, GrantedBy = grantedBy };
            db.SubscriberBanecoAccounts.Add(grant);
        }

        if (request.IsDefault)
        {
            // El índice único filtrado exige que la anterior predeterminada se libere en la misma transacción.
            var others = await db.SubscriberBanecoAccounts
                .Where(x => x.SubscriberId == subscriberId && x.BanecoAccountId != request.AccountId && x.IsDefault)
                .ToListAsync(ct);
            foreach (var other in others) other.IsDefault = false;
        }

        grant.IsDefault = request.IsDefault;
        await db.SaveChangesAsync(ct);

        return new GrantResult(GrantStatus.Granted, new GrantedAccountSummary(account.Id, account.Code, account.Name,
            grant.IsDefault, account.IsActive, grant.GrantedAt, grant.GrantedBy));
    }

    public async Task<bool> RevokeGrantAsync(Guid subscriberId, Guid accountId, CancellationToken ct)
    {
        var grant = await db.SubscriberBanecoAccounts
            .SingleOrDefaultAsync(x => x.SubscriberId == subscriberId && x.BanecoAccountId == accountId, ct);
        if (grant is null) return false;

        db.SubscriberBanecoAccounts.Remove(grant);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<BanecoAccount> AccountsWithSubscribers() => db.BanecoAccounts.AsNoTracking()
        .Include(x => x.Subscribers).ThenInclude(x => x.Subscriber);

    /// <summary>Cifra el valor, o lo borra si llega vacío. Una cadena vacía es la forma de dar de baja una credencial.</summary>
    private string? ProtectOrNull(string? plaintext) =>
        string.IsNullOrWhiteSpace(plaintext) ? null : protector.Protect(plaintext);

    /// <summary>Expone si la cuenta tiene configurados sus secretos, nunca su valor.</summary>
    private static BanecoAccountSummary ToSummary(BanecoAccount account) => new(account.Id, account.Code, account.Name,
        account.CredentialRef, account.IsActive,
        !string.IsNullOrWhiteSpace(account.AccountCodeEncrypted),
        !string.IsNullOrWhiteSpace(account.BatchDebitAccountEncrypted),
        !string.IsNullOrWhiteSpace(account.AuthUserNameEncrypted) && !string.IsNullOrWhiteSpace(account.AuthPasswordEncrypted),
        account.CredentialsVerifiedAt,
        account.CreatedAt,
        account.Subscribers
            .Where(s => s.Subscriber is not null)
            .Select(s => new AccountSubscriberSummary(s.SubscriberId, s.Subscriber!.Code, s.Subscriber.Name, s.IsDefault, s.GrantedAt))
            .OrderBy(s => s.Code, StringComparer.Ordinal)
            .ToList());
}
