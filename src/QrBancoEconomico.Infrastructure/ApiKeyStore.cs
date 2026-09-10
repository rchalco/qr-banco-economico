using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QrBancoEconomico.Application;

namespace QrBancoEconomico.Infrastructure;

public sealed class ApiKeyStore(
    BanecoDbContext db,
    IMemoryCache cache,
    IOptions<ApiKeySecurityOptions> options,
    ILogger<ApiKeyStore> logger) : IApiKeyStore
{
    private readonly ApiKeySecurityOptions _options = options.Value;

    public async Task<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken ct)
    {
        if (!ApiKeyMaterial.TryParse(presentedKey, out var apiKey))
            return ApiKeyValidationResult.Rejected(ApiKeyFailure.Malformed);

        var snapshot = await GetSnapshotAsync(apiKey, ct);
        if (snapshot is null)
            return ApiKeyValidationResult.Rejected(ApiKeyFailure.Unknown);

        // El ambiente ya no viaja en la clave: lo impone la fila, para que una clave de dev no sirva
        // en una instancia prd aunque ambas apunten a la misma base.
        if (!string.Equals(snapshot.Environment, _options.Environment, StringComparison.OrdinalIgnoreCase))
            return ApiKeyValidationResult.Rejected(ApiKeyFailure.EnvironmentMismatch);

        if (snapshot.RevokedAt is not null)
            return ApiKeyValidationResult.Rejected(ApiKeyFailure.Revoked);
        if (snapshot.ExpiresAt is not null && snapshot.ExpiresAt <= DateTimeOffset.UtcNow)
            return ApiKeyValidationResult.Rejected(ApiKeyFailure.Expired);
        if (!snapshot.SubscriberActive)
            return ApiKeyValidationResult.Rejected(ApiKeyFailure.SubscriberDisabled);

        return ApiKeyValidationResult.Success(new AuthenticatedSubscriber(snapshot.SubscriberId, snapshot.SubscriberCode,
            snapshot.SubscriberName, snapshot.ApiKeyId, snapshot.Scopes, snapshot.RequestsPerMinute));
    }

    public async Task RegisterUseAsync(Guid apiKeyId, CancellationToken ct)
    {
        var usageKey = ApiKeyCache.UsageKeyFor(apiKeyId);
        if (cache.TryGetValue(usageKey, out _)) return;

        cache.Set(usageKey, true, TimeSpan.FromMinutes(Math.Max(1, _options.UsageStampMinutes)));
        try
        {
            await db.SubscriberApiKeys
                .Where(x => x.Id == apiKeyId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAt, DateTimeOffset.UtcNow), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El sello de uso es telemetría: su fallo no debe tumbar una solicitud autenticada.
            logger.LogWarning(ex, "No se pudo actualizar LastUsedAt de la API Key {ApiKeyId}.", apiKeyId);
        }
    }

    /// <summary>
    /// Resuelve la fila de la clave. La caché vive en memoria del proceso, de modo que una clave
    /// insertada directamente en la base tarda hasta <c>ApiKeys:CacheSeconds</c> en ser reconocida,
    /// igual que una revocación tarda ese mismo tiempo en propagarse entre instancias.
    /// </summary>
    private async Task<KeySnapshot?> GetSnapshotAsync(Guid apiKey, CancellationToken ct)
    {
        var cacheKey = ApiKeyCache.KeyFor(apiKey);
        if (cache.TryGetValue<CachedKey>(cacheKey, out var cached) && cached is not null)
            return cached.Snapshot;

        var row = await db.SubscriberApiKeys.AsNoTracking()
            .Where(x => x.ApiKey == apiKey)
            .Select(x => new
            {
                x.Id, x.SubscriberId, SubscriberCode = x.Subscriber!.Code, SubscriberName = x.Subscriber.Name,
                x.Subscriber.IsActive, x.Subscriber.RequestsPerMinute, x.Environment, x.Scopes,
                x.ExpiresAt, x.RevokedAt
            })
            .SingleOrDefaultAsync(ct);

        var snapshot = row is null
            ? null
            : new KeySnapshot(row.Id, row.SubscriberId, row.SubscriberCode, row.SubscriberName, row.IsActive,
                row.RequestsPerMinute, row.Environment,
                row.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries), row.ExpiresAt, row.RevokedAt);

        // También se cachea la ausencia, para que un barrido de claves inválidas no golpee la base en cada intento.
        cache.Set(cacheKey, new CachedKey(snapshot), TimeSpan.FromSeconds(Math.Max(1, _options.CacheSeconds)));
        return snapshot;
    }

    private sealed record CachedKey(KeySnapshot? Snapshot);

    private sealed record KeySnapshot(Guid ApiKeyId, Guid SubscriberId, string SubscriberCode, string SubscriberName,
        bool SubscriberActive, int RequestsPerMinute, string Environment, string[] Scopes,
        DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt);
}
