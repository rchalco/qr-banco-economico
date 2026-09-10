namespace QrBancoEconomico.Application;

/// <summary>
/// Resuelve la credencial que viaja hacia Baneco. Es un plano distinto al de la API Key del proxy:
/// aquella autentica al suscriptor ante nosotros, esta autentica al proxy ante el banco.
/// </summary>
public interface IBanecoTokenProvider
{
    Task<string?> GetBearerTokenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Descarta el token en uso y obtiene uno nuevo. Se invoca cuando el banco responde 401, que es la
    /// señal de que el token venció antes de lo previsto. Devuelve false si no hay nada que renovar
    /// (modo pass-through o respaldo de configuración): en ese caso el 401 debe propagarse.
    /// </summary>
    Task<bool> TryRenewAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Obtiene y mantiene vigente el token de Baneco de cada cuenta del inventario. El banco lo emite con
/// ~30 minutos de vida, de modo que se renueva solo: nunca se pide al operador que lo pegue a mano.
/// El token vive únicamente en memoria; jamás se persiste.
/// </summary>
public interface IBanecoTokenService
{
    /// <summary>Token vigente de la cuenta, o null si la cuenta no tiene credenciales configuradas.</summary>
    Task<string?> GetTokenAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Descarta el token cacheado de la cuenta para forzar una nueva autenticación.</summary>
    void Invalidate(Guid accountId);

    /// <summary>Comprueba credenciales sin exponer el token. Uso administrativo.</summary>
    Task<BanecoCredentialCheck> VerifyAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <summary>
/// Cifra y descifra los secretos que se guardan en la base. La llave maestra vive en un archivo fuera
/// del repositorio; ver <c>Baneco:MasterKeyFile</c>.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    /// <exception cref="ProtectedSecretException">Si el valor no se puede descifrar.</exception>
    string Unprotect(string ciphertext);
}

/// <summary>
/// Un secreto guardado en la base no se puede descifrar. Las dos causas reales son que el valor se
/// cargó con un <c>INSERT</c> sin pasar por la API —y por tanto quedó sin cifrar— o que la llave
/// maestra actual no es la que lo cifró. En ambos casos es un problema de aprovisionamiento, no del
/// llamante: se traduce a 409 con instrucciones en lugar de a un 500 opaco.
/// </summary>
public sealed class ProtectedSecretException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Cuenta de Baneco asociada a la solicitud en curso, una vez resuelta contra el inventario.</summary>
public interface IBanecoAccountContext
{
    ResolvedBanecoAccount? Current { get; }
    void Set(ResolvedBanecoAccount account);
}

/// <summary>Decide qué cuenta de Baneco atiende la solicitud de un suscriptor y a qué cuenta acredita.</summary>
public interface IBanecoAccountResolver
{
    Task<BanecoAccountResolution> ResolveAsync(Guid subscriberId, string? requestedAccountCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<GrantedAccountSummary>> ListGrantedAsync(Guid subscriberId, CancellationToken cancellationToken);

    /// <summary>
    /// Cuenta de ahorro del suscriptor lista para enviar al banco: se descifra la capa de la llave
    /// maestra y queda el criptograma que Baneco espera en <c>accountCredit</c>. Null si no tiene ninguna
    /// cargada, que es un alta incompleta y no una condición operativa normal.
    /// </summary>
    Task<string?> ResolveAccountCreditAsync(Guid subscriberId, CancellationToken cancellationToken);
}

/// <summary>Inventario N-N de cuentas de Baneco y de su concesión a suscriptores.</summary>
public interface IBanecoAccountInventory
{
    Task<IReadOnlyList<BanecoAccountSummary>> ListAccountsAsync(CancellationToken cancellationToken);
    Task<BanecoAccountSummary?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken);
    /// <summary>Devuelve null si el <c>code</c> ya está tomado.</summary>
    Task<BanecoAccountSummary?> CreateAccountAsync(CreateBanecoAccountRequest request, CancellationToken cancellationToken);
    Task<BanecoAccountSummary?> UpdateAccountAsync(Guid accountId, UpdateBanecoAccountRequest request, CancellationToken cancellationToken);
    Task<GrantResult> GrantAsync(Guid subscriberId, GrantAccountRequest request, string? grantedBy, CancellationToken cancellationToken);
    Task<bool> RevokeGrantAsync(Guid subscriberId, Guid accountId, CancellationToken cancellationToken);
}

/// <summary>Suscriptor asociado a la solicitud en curso. Resuelto por la autenticación de API Key.</summary>
public interface ISubscriberContext
{
    Guid SubscriberId { get; }
    string Code { get; }
    IReadOnlySet<string> Scopes { get; }
}

/// <summary>Valida las API Keys presentadas y registra su uso.</summary>
public interface IApiKeyStore
{
    Task<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken cancellationToken);
    Task RegisterUseAsync(Guid apiKeyId, CancellationToken cancellationToken);
}

/// <summary>Alta y baja de suscriptores y de sus credenciales.</summary>
public interface ISubscriberProvisioning
{
    Task<IReadOnlyList<SubscriberSummary>> ListSubscribersAsync(CancellationToken cancellationToken);
    Task<SubscriberSummary?> GetSubscriberAsync(Guid subscriberId, CancellationToken cancellationToken);
    /// <summary>Devuelve null si el <c>code</c> ya está tomado.</summary>
    Task<SubscriberSummary?> CreateSubscriberAsync(CreateSubscriberRequest request, CancellationToken cancellationToken);
    Task<SubscriberSummary?> UpdateSubscriberAsync(Guid subscriberId, UpdateSubscriberRequest request, CancellationToken cancellationToken);
    Task<IssuedApiKey?> IssueApiKeyAsync(Guid subscriberId, IssueApiKeyRequest request, CancellationToken cancellationToken);
    Task<bool> RevokeApiKeyAsync(Guid subscriberId, Guid apiKeyId, CancellationToken cancellationToken);
}

/// <summary>
/// Cliente de Baneco. Solo cubre las dos operaciones que el proxy expone: emitir un QR y listar los
/// QR cobrados. El resto de la API del banco queda deliberadamente fuera para no ampliar la superficie.
/// </summary>
public interface IBanecoGateway
{
    Task<GenerateQrResponse> GenerateQrAsync(BanecoGenerateQrRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentQr>> GetPaidQrsAsync(DateOnly date, CancellationToken cancellationToken);
}
