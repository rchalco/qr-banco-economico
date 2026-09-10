using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QrBancoEconomico.Application;
using QrBancoEconomico.Domain;

namespace QrBancoEconomico.Infrastructure;

public sealed class SubscriberProvisioning(
    BanecoDbContext db,
    IMemoryCache cache,
    ISecretProtector protector,
    IOptions<ApiKeySecurityOptions> options) : ISubscriberProvisioning
{
    private readonly ApiKeySecurityOptions _options = options.Value;

    public async Task<IReadOnlyList<SubscriberSummary>> ListSubscribersAsync(CancellationToken ct)
    {
        var subscribers = await WithDetails(db.Subscribers.AsNoTracking())
            .OrderBy(x => x.Code)
            .ToListAsync(ct);
        return subscribers.Select(ToSummary).ToList();
    }

    public async Task<SubscriberSummary?> GetSubscriberAsync(Guid subscriberId, CancellationToken ct)
    {
        var subscriber = await WithDetails(db.Subscribers.AsNoTracking())
            .SingleOrDefaultAsync(x => x.Id == subscriberId, ct);
        return subscriber is null ? null : ToSummary(subscriber);
    }

    /// <summary>Devuelve null si el <c>code</c> ya está tomado.</summary>
    public async Task<SubscriberSummary?> CreateSubscriberAsync(CreateSubscriberRequest request, CancellationToken ct)
    {
        if (await db.Subscribers.AnyAsync(x => x.Code == request.Code, ct)) return null;

        var subscriber = new Subscriber
        {
            Code = request.Code,
            Name = request.Name,
            ContactEmail = request.ContactEmail,
            SavingsAccountEncrypted = ProtectSavingsAccount(request.SavingsAccount),
            RequestsPerMinute = request.RequestsPerMinute
        };
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync(ct);
        return ToSummary(subscriber);
    }

    /// <summary>Devuelve null si el suscriptor no existe.</summary>
    public async Task<SubscriberSummary?> UpdateSubscriberAsync(Guid subscriberId, UpdateSubscriberRequest request, CancellationToken ct)
    {
        var subscriber = await WithDetails(db.Subscribers).SingleOrDefaultAsync(x => x.Id == subscriberId, ct);
        if (subscriber is null) return null;

        if (request.Name is not null) subscriber.Name = request.Name;
        if (request.ContactEmail is not null) subscriber.ContactEmail = request.ContactEmail;
        if (request.SavingsAccount is not null) subscriber.SavingsAccountEncrypted = ProtectSavingsAccount(request.SavingsAccount);
        if (request.IsActive is not null) subscriber.IsActive = request.IsActive.Value;
        if (request.RequestsPerMinute is not null) subscriber.RequestsPerMinute = request.RequestsPerMinute.Value;
        await db.SaveChangesAsync(ct);

        // La desactivación debe surtir efecto sin esperar al vencimiento de la caché de validación.
        foreach (var key in subscriber.ApiKeys) cache.Remove(ApiKeyCache.KeyFor(key.ApiKey));
        return ToSummary(subscriber);
    }

    /// <summary>Devuelve null si el suscriptor no existe. La clave en claro solo viaja en esta respuesta.</summary>
    public async Task<IssuedApiKey?> IssueApiKeyAsync(Guid subscriberId, IssueApiKeyRequest request, CancellationToken ct)
    {
        var unknownScopes = request.Scopes.Where(s => !ApiScopes.All.Contains(s)).ToArray();
        if (unknownScopes.Length > 0)
            throw new ArgumentException($"Alcances no reconocidos: {string.Join(", ", unknownScopes)}.", nameof(request));

        if (!await db.Subscribers.AnyAsync(x => x.Id == subscriberId, ct)) return null;

        var apiKey = new SubscriberApiKey
        {
            SubscriberId = subscriberId,
            ApiKey = ApiKeyMaterial.Create(),
            Environment = _options.Environment,
            Scopes = string.Join(' ', request.Scopes.Distinct(StringComparer.Ordinal)),
            Label = request.Label,
            ExpiresAt = request.ExpiresAt
        };
        db.SubscriberApiKeys.Add(apiKey);
        await db.SaveChangesAsync(ct);

        cache.Remove(ApiKeyCache.KeyFor(apiKey.ApiKey));
        return new IssuedApiKey(ToSummary(apiKey), apiKey.ApiKey);
    }

    public async Task<bool> RevokeApiKeyAsync(Guid subscriberId, Guid apiKeyId, CancellationToken ct)
    {
        var apiKey = await db.SubscriberApiKeys
            .SingleOrDefaultAsync(x => x.Id == apiKeyId && x.SubscriberId == subscriberId, ct);
        if (apiKey is null || apiKey.RevokedAt is not null) return false;

        apiKey.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        cache.Remove(ApiKeyCache.KeyFor(apiKey.ApiKey));
        return true;
    }

    /// <summary>
    /// Valida y cifra la cuenta de ahorro. Debe llegar ya cifrada por Baneco y en Base64: la validación
    /// ocurre aquí, en el alta, y no en cada emisión de QR. Una cadena vacía la da de baja.
    /// </summary>
    /// <exception cref="ArgumentException">Si el valor no es Base64 válido.</exception>
    private string? ProtectSavingsAccount(string? savingsAccount)
    {
        if (string.IsNullOrWhiteSpace(savingsAccount)) return null;

        var trimmed = savingsAccount.Trim();
        if (!Convert.TryFromBase64String(trimmed, new byte[trimmed.Length], out _))
            throw new ArgumentException(
                "savingsAccount debe ser la cuenta cifrada con la clave AES de Baneco, codificada en Base64.",
                nameof(savingsAccount));

        return protector.Protect(trimmed);
    }

    private static IQueryable<Subscriber> WithDetails(IQueryable<Subscriber> subscribers) => subscribers
        .Include(x => x.ApiKeys)
        .Include(x => x.Accounts).ThenInclude(x => x.BanecoAccount);

    /// <summary>Expone si el suscriptor tiene cuenta de ahorro cargada, nunca su valor.</summary>
    private static SubscriberSummary ToSummary(Subscriber subscriber) => new(subscriber.Id, subscriber.Code,
        subscriber.Name, subscriber.ContactEmail, subscriber.IsActive,
        !string.IsNullOrWhiteSpace(subscriber.SavingsAccountEncrypted), subscriber.RequestsPerMinute,
        subscriber.CreatedAt, subscriber.ApiKeys.Select(ToSummary).ToList(),
        subscriber.Accounts
            .Where(a => a.BanecoAccount is not null)
            .Select(a => new GrantedAccountSummary(a.BanecoAccountId, a.BanecoAccount!.Code, a.BanecoAccount.Name,
                a.IsDefault, a.BanecoAccount.IsActive, a.GrantedAt, a.GrantedBy))
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .ToList());

    /// <summary>No expone el valor de la clave: para conocerlo hay que leer la tabla o emitir otra.</summary>
    private static ApiKeySummary ToSummary(SubscriberApiKey key) => new(key.Id, key.Environment,
        key.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries), key.Label, key.CreatedAt, key.ExpiresAt,
        key.RevokedAt, key.LastUsedAt);
}
