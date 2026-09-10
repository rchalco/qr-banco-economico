using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QrBancoEconomico.Application;

namespace QrBancoEconomico.Infrastructure;

/// <summary>
/// Mantiene vigente el token de cada cuenta del inventario. Baneco lo emite con ~30 minutos de vida,
/// así que se renueva de forma autónoma a partir de las credenciales cifradas en la base: nadie tiene
/// que pegar un token en un archivo de configuración ni reiniciar el servicio cuando vence.
/// </summary>
/// <remarks>
/// La caché es por proceso. Con varias instancias cada una autentica por su cuenta; si Baneco resultara
/// ser de sesión única (emitir un token invalida el anterior) hay que moverla a una caché distribuida
/// con lock. Está pendiente de confirmar con el banco.
/// </remarks>
public sealed class BanecoTokenService(
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    ISecretProtector protector,
    IOptions<BanecoOptions> options,
    ILogger<BanecoTokenService> logger,
    TimeProvider clock) : IBanecoTokenService
{
    /// <summary>Nombre del cliente HTTP dedicado. Es distinto al del gateway para no recursar sobre el propio proveedor de token.</summary>
    public const string HttpClientName = "baneco-auth";

    private readonly BanecoOptions _options = options.Value;
    private readonly ConcurrentDictionary<Guid, CachedToken> _tokens = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public async Task<string?> GetTokenAsync(Guid accountId, CancellationToken ct)
    {
        if (_tokens.TryGetValue(accountId, out var cached) && cached.IsUsableAt(clock.GetUtcNow()))
            return cached.Token;

        // Un solo hilo por cuenta autentica; los demás esperan y reutilizan el resultado. Sin esto, una
        // ráfaga tras el vencimiento dispararía tantas autenticaciones como solicitudes concurrentes.
        var gate = _gates.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_tokens.TryGetValue(accountId, out cached) && cached.IsUsableAt(clock.GetUtcNow()))
                return cached.Token;

            var issued = await AuthenticateAsync(accountId, ct);
            if (issued is null) return null;

            _tokens[accountId] = issued;
            return issued.Token;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate(Guid accountId) => _tokens.TryRemove(accountId, out _);

    public async Task<BanecoCredentialCheck> VerifyAsync(Guid accountId, CancellationToken ct)
    {
        Invalidate(accountId);
        try
        {
            var issued = await AuthenticateAsync(accountId, ct);
            return issued is null
                ? new BanecoCredentialCheck(false, "La cuenta no tiene usuario y contraseña de Baneco cargados.", null)
                : new BanecoCredentialCheck(true, "Autenticación correcta.", issued.ExpiresAt);
        }
        catch (ProtectedSecretException ex)
        {
            // Credenciales cargadas sin cifrar, o cifradas con otra llave maestra.
            return new BanecoCredentialCheck(false,
                $"{ex.Message} Vuelva a cargar usuario y contraseña con PATCH /api/admin/baneco-accounts/{{id}}.", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            // El mensaje va al operador: describe el fallo sin filtrar credenciales ni el cuerpo del banco.
            return new BanecoCredentialCheck(false, ex.Message, null);
        }
    }

    /// <summary>Autentica contra el banco y devuelve el token. Null si la cuenta no tiene credenciales cargadas.</summary>
    private async Task<CachedToken?> AuthenticateAsync(Guid accountId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BanecoDbContext>();

        var row = await db.BanecoAccounts.AsNoTracking()
            .Where(x => x.Id == accountId)
            .Select(x => new { x.Code, x.AuthUserNameEncrypted, x.AuthPasswordEncrypted })
            .SingleOrDefaultAsync(ct);

        if (row is null) throw new InvalidOperationException($"La cuenta de Baneco {accountId} no existe.");
        if (string.IsNullOrWhiteSpace(row.AuthUserNameEncrypted) || string.IsNullOrWhiteSpace(row.AuthPasswordEncrypted))
            return null;

        var userName = protector.Unprotect(row.AuthUserNameEncrypted);
        var password = protector.Unprotect(row.AuthPasswordEncrypted);

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync(_options.AuthenticatePath, new { userName, password }, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // El cuerpo puede traer el eco de las credenciales: se registra el estado, nunca el contenido.
            logger.LogError("Baneco rechazó la autenticación de la cuenta {AccountCode} con HTTP {StatusCode}.",
                row.Code, (int)response.StatusCode);
            throw new InvalidOperationException(
                $"Baneco rechazó la autenticación de la cuenta '{row.Code}' con HTTP {(int)response.StatusCode}.");
        }

        var token = ExtractToken(payload)
            ?? throw new InvalidOperationException(
                $"La respuesta de autenticación de Baneco para '{row.Code}' no contiene un token reconocible. " +
                "Ajuste BanecoTokenService.TokenFieldNames al nombre que use el banco.");

        var now = clock.GetUtcNow();
        var expiresAt = ReadJwtExpiry(token) ?? now.AddMinutes(_options.TokenLifetimeMinutes);
        var renewAt = expiresAt.AddSeconds(-_options.TokenRenewMarginSeconds);
        if (renewAt <= now)
        {
            // Margen mayor que la vida del token: se renovaría en cada solicitud. Se acota a la mitad.
            renewAt = now.AddSeconds((expiresAt - now).TotalSeconds / 2);
        }

        logger.LogInformation("Token de Baneco renovado para la cuenta {AccountCode}; vence {ExpiresAt:O}, se renovará {RenewAt:O}.",
            row.Code, expiresAt, renewAt);

        await db.BanecoAccounts.Where(x => x.Id == accountId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CredentialsVerifiedAt, now), ct);

        return new CachedToken(token, expiresAt, renewAt);
    }

    /// <summary>Nombres bajo los que el banco puede devolver el token. Baneco documenta <c>token</c>.</summary>
    private static readonly string[] TokenFieldNames =
        ["token", "accessToken", "access_token", "jwtToken", "jwt", "authToken", "bearerToken"];

    private static string? ExtractToken(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        return FindToken(root) ?? WrappedObjects(root).Select(FindToken).FirstOrDefault(value => value is not null);
    }

    private static string? FindToken(JsonElement element)
    {
        foreach (var name in TokenFieldNames)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var token = value.GetString();
                if (!string.IsNullOrWhiteSpace(token)) return token;
            }
        }

        return null;
    }

    /// <summary>Algunas pasarelas envuelven la carga útil; se mira un nivel hacia dentro antes de rendirse.</summary>
    private static IEnumerable<JsonElement> WrappedObjects(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object) yield return property.Value;
        }
    }

    /// <summary>Lee <c>exp</c> del JWT. Null si el token no es un JWT o no trae vencimiento.</summary>
    private static DateTimeOffset? ReadJwtExpiry(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var payload = Base64Url.DecodeFromChars(parts[1]);
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <param name="RenewAt">Momento a partir del cual se renueva: vencimiento menos el margen de seguridad.</param>
    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt, DateTimeOffset RenewAt)
    {
        public bool IsUsableAt(DateTimeOffset now) => now < RenewAt;
    }
}
