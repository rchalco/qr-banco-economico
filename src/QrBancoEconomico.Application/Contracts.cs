using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace QrBancoEconomico.Application;

public record ApiResult(int ResponseCode, string Message);
/// <summary>
/// Solicitud de emisión de QR. No lleva <c>accountCredit</c>: la cuenta de ahorro a acreditar es la del
/// suscriptor, guardada cifrada en su alta. Aceptarla por el cuerpo permitiría que un suscriptor
/// acreditara la cuenta de otro con solo conocer su criptograma.
/// Rechaza cualquier campo desconocido: si un integrador sigue enviando <c>accountCredit</c> recibe un
/// 400 explícito en lugar de que el proxy lo ignore en silencio y acredite una cuenta distinta a la que espera.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record GenerateQrRequest(
    [Required] string TransactionId, [Required] string Currency,
    decimal Amount, string? Description, DateOnly DueDate, bool SingleUse, bool ModifyAmount, string? BranchCode) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ModifyAmount && Amount != 0m)
            yield return new ValidationResult("amount debe ser 0 cuando modifyAmount es true.", [nameof(Amount)]);

        if (!ModifyAmount && Amount <= 0m)
            yield return new ValidationResult("amount debe ser mayor que 0 cuando modifyAmount es false.", [nameof(Amount)]);
    }
}
/// <summary>
/// Cuerpo que viaja a Baneco. Existe para separar el contrato de entrada del contrato del banco: el
/// suscriptor no envía <c>accountCredit</c> y el banco sí lo exige, así que el proxy lo agrega aquí con
/// la cuenta de ahorro del suscriptor.
/// </summary>
public sealed record BanecoGenerateQrRequest(string TransactionId, string AccountCredit, string Currency,
    decimal Amount, string? Description, DateOnly DueDate, bool SingleUse, bool ModifyAmount, string? BranchCode)
{
    public BanecoGenerateQrRequest(GenerateQrRequest request, string accountCredit)
        : this(request.TransactionId, accountCredit, request.Currency, request.Amount, request.Description,
            request.DueDate, request.SingleUse, request.ModifyAmount, request.BranchCode)
    {
    }
}

public record GenerateQrResponse(int ResponseCode, string Message, string? QrId, string? QrImage);
public record PaymentQr(string QrId, string? TransactionId, DateOnly PaymentDate, string? PaymentTime,
    string Currency, decimal Amount, string? SenderBankCode, string? SenderName, string? SenderDocumentId,
    string? SenderAccount, string? Description, string? BranchCode);
public record QrStatusResponse(int ResponseCode, string Message, int StatusQrCode, IReadOnlyList<PaymentQr> Payment);
/// <param name="AccountCode">Se omite cuando el suscriptor opera una cuenta del inventario; ver <see cref="GenerateQrRequest"/>.</param>
public record AccountHistoryRequest(string? AccountCode, DateOnly StartDate, DateOnly EndDate);
public record BatchPaymentItem([Required] string BatchDetailId, [Range(typeof(decimal), "0.01", "5000.00", ParseLimitsInInvariantCulture = true)] decimal Amount,
    [Required] string AccountCode, [Required] string AccountTypeCode, [Required] string BankCode,
    [Required] string BeneficiaryName, string? BeneficiaryDocId, string? BeneficiaryPhone, string? BeneficiaryEmail,
    string? Note, string? AmlSource, string? AmlDestination);
/// <param name="AccountCode">Cuenta a debitar. Se omite cuando el suscriptor opera una cuenta del inventario.</param>
public record BatchUploadRequest([Required] string BatchId, [Required] string Type, [Required] string Description,
    bool DetailedDebit, string? AccountCode, [Required] string BatchCurrency,
    [Range(typeof(decimal), "0.01", "5000.00", ParseLimitsInInvariantCulture = true)] decimal BatchAmount, [Required] string AmlSource,
    [Required] string AmlDestination, [MinLength(1)] IReadOnlyList<BatchPaymentItem> PaymentList);
public record BatchUploadResponse(int ResponseCode, string Message, long? BankBatchId);
public record PaymentNotification(PaymentQr Payment);

/// <summary>
/// Exige que el valor sea Base64 válido. Se aplica a los criptogramas que produce la clave AES de Baneco,
/// para que un valor mal copiado se rechace con 400 en el alta y no como un fallo del banco en la
/// primera operación real. Vacío se acepta: es la forma de dar de baja el dato.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class Base64CiphertextAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text)) return true;

        var trimmed = text.Trim();
        return Convert.TryFromBase64String(trimmed, new byte[trimmed.Length], out _);
    }

    public override string FormatErrorMessage(string name) =>
        $"{name} debe estar cifrada con la clave AES de Baneco y codificada en Base64.";
}

/// <summary>Alcances que puede otorgarse a una API Key. `admin` no implica los demás: debe concederse explícitamente.</summary>
public static class ApiScopes
{
    public const string CryptoUse = "crypto:use";
    public const string BanecoAuth = "baneco:auth";
    public const string QrWrite = "qr:write";
    public const string QrRead = "qr:read";
    public const string AccountsRead = "accounts:read";
    public const string BatchesWrite = "batches:write";
    public const string NotificationsWrite = "notifications:write";
    public const string Admin = "admin";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        CryptoUse, BanecoAuth, QrWrite, QrRead, AccountsRead, BatchesWrite, NotificationsWrite, Admin
    };
}

public enum ApiKeyFailure
{
    None = 0,
    Malformed,
    Unknown,
    Revoked,
    Expired,
    SubscriberDisabled,
    EnvironmentMismatch
}

/// <param name="ApiKeyId">Identificador interno de la fila de la clave. Es el que va a los logs; no autentica.</param>
public sealed record AuthenticatedSubscriber(Guid SubscriberId, string Code, string Name, Guid ApiKeyId,
    IReadOnlyList<string> Scopes, int RequestsPerMinute);

public sealed record ApiKeyValidationResult(AuthenticatedSubscriber? Subscriber, ApiKeyFailure Failure)
{
    public static ApiKeyValidationResult Success(AuthenticatedSubscriber subscriber) => new(subscriber, ApiKeyFailure.None);
    public static ApiKeyValidationResult Rejected(ApiKeyFailure failure) => new(null, failure);
}

/// <param name="SavingsAccount">
/// Cuenta de ahorro a acreditar, cifrada con la clave AES de Baneco y en Base64 (el mismo valor que el
/// banco llama <c>accountCredit</c>). El proxy la vuelve a cifrar con su llave maestra antes de
/// guardarla y no la devuelve nunca. Sin ella el suscriptor no puede emitir QR.
/// </param>
public record CreateSubscriberRequest([Required, RegularExpression("^[a-z0-9][a-z0-9-]{2,49}$",
        ErrorMessage = "code debe ser minúsculas, dígitos o guiones, entre 3 y 50 caracteres.")] string Code,
    [Required, MaxLength(200)] string Name, [EmailAddress] string? ContactEmail,
    [MaxLength(512)][property: Base64Ciphertext] string? SavingsAccount,
    [Range(1, 100000)] int RequestsPerMinute = 120);

/// <param name="SavingsAccount">Reemplaza la cuenta de ahorro si se envía; la deja como está si se omite.</param>
public record UpdateSubscriberRequest([MaxLength(200)] string? Name, [EmailAddress] string? ContactEmail,
    [MaxLength(512)][property: Base64Ciphertext] string? SavingsAccount, bool? IsActive, [Range(1, 100000)] int? RequestsPerMinute);

public record IssueApiKeyRequest([MinLength(1)] IReadOnlyList<string> Scopes, [MaxLength(100)] string? Label,
    DateTimeOffset? ExpiresAt);

/// <summary>Estado de una clave. No incluye su valor: para conocerlo hay que leer la tabla o volver a emitirla.</summary>
public record ApiKeySummary(Guid Id, string Environment, IReadOnlyList<string> Scopes, string? Label,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt, DateTimeOffset? LastUsedAt);

/// <param name="HasSavingsAccount">Si el suscriptor tiene cargada su cuenta de ahorro. Nunca se devuelve su valor.</param>
public record SubscriberSummary(Guid Id, string Code, string Name, string? ContactEmail, bool IsActive,
    bool HasSavingsAccount, int RequestsPerMinute, DateTimeOffset CreatedAt, IReadOnlyList<ApiKeySummary> ApiKeys,
    IReadOnlyList<GrantedAccountSummary> Accounts);

/// <param name="ApiKey">
/// El GUID que el suscriptor presentará en <c>X-Api-Key</c>. Se devuelve aquí por comodidad; a diferencia
/// del diseño anterior también queda legible en la base, así que esta respuesta ya no es la única copia.
/// </param>
public record IssuedApiKey(ApiKeySummary Key, Guid ApiKey);

// ---------------------------------------------------------------------------
// Inventario N-N de cuentas de Baneco
// ---------------------------------------------------------------------------

/// <summary>
/// Cuenta de Baneco resuelta para la solicitud en curso: identifica <b>con qué credencial</b> se habla con
/// el banco. No transporta secretos ni la cuenta a acreditar, que es la del suscriptor.
/// </summary>
public sealed record ResolvedBanecoAccount(Guid Id, string Code, string Name, string? CredentialRef);

public enum BanecoAccountResolutionStatus
{
    /// <summary>El suscriptor opera una cuenta del inventario.</summary>
    Resolved = 0,
    /// <summary>El suscriptor no tiene cuentas concedidas: aporta sus propias credenciales de banco.</summary>
    PassThrough,
    /// <summary>Pidió una cuenta que no existe o que no tiene concedida.</summary>
    NotGranted,
    /// <summary>Tiene varias cuentas y no indicó cuál usar ni tiene una predeterminada.</summary>
    Ambiguous,
    /// <summary>La cuenta existe y está concedida, pero fue dada de baja.</summary>
    Inactive
}

public sealed record BanecoAccountResolution(ResolvedBanecoAccount? Account, BanecoAccountResolutionStatus Status)
{
    public static BanecoAccountResolution Resolved(ResolvedBanecoAccount account) => new(account, BanecoAccountResolutionStatus.Resolved);
    public static BanecoAccountResolution Rejected(BanecoAccountResolutionStatus status) => new(null, status);
}

public record GrantedAccountSummary(Guid AccountId, string Code, string Name, bool IsDefault, bool IsActive,
    DateTimeOffset GrantedAt, string? GrantedBy);

public record AccountSubscriberSummary(Guid SubscriberId, string Code, string Name, bool IsDefault, DateTimeOffset GrantedAt);

/// <param name="HasCredentials">
/// Si la cuenta tiene usuario y contraseña de Baneco cargados. Nunca se devuelve su valor: el usuario
/// y la contraseña salen de la base solo hacia el servicio que autentica contra el banco.
/// </param>
public record BanecoAccountSummary(Guid Id, string Code, string Name, string? CredentialRef, bool IsActive,
    bool HasAccountCode, bool HasBatchDebitAccount, bool HasCredentials, DateTimeOffset? CredentialsVerifiedAt,
    DateTimeOffset CreatedAt, IReadOnlyList<AccountSubscriberSummary> Subscribers);

/// <param name="AuthUserName">Usuario de Baneco. Se cifra antes de guardarse y no vuelve a exponerse.</param>
/// <param name="AuthPassword">Contraseña de Baneco. Se cifra antes de guardarse y no vuelve a exponerse.</param>
public record CreateBanecoAccountRequest(
    [Required, RegularExpression("^[a-z0-9][a-z0-9-]{2,49}$",
        ErrorMessage = "code debe ser minúsculas, dígitos o guiones, entre 3 y 50 caracteres.")] string Code,
    [Required, MaxLength(200)] string Name,
    [MaxLength(100)] string? CredentialRef,
    [MaxLength(1024)][property: Base64Ciphertext] string? AccountCodeEncrypted,
    [MaxLength(1024)][property: Base64Ciphertext] string? BatchDebitAccountEncrypted,
    [MaxLength(200)] string? AuthUserName,
    [MaxLength(400)] string? AuthPassword);

/// <param name="AuthUserName">Deja el usuario como está si se omite; lo reemplaza si se envía.</param>
/// <param name="AuthPassword">Deja la contraseña como está si se omite; la reemplaza si se envía.</param>
public record UpdateBanecoAccountRequest([MaxLength(200)] string? Name, [MaxLength(100)] string? CredentialRef,
    [MaxLength(1024)][property: Base64Ciphertext] string? AccountCodeEncrypted,
    [MaxLength(1024)][property: Base64Ciphertext] string? BatchDebitAccountEncrypted, [MaxLength(200)] string? AuthUserName, [MaxLength(400)] string? AuthPassword, bool? IsActive);

/// <summary>Resultado de probar las credenciales de una cuenta contra Baneco. No transporta el token.</summary>
public record BanecoCredentialCheck(bool Succeeded, string Message, DateTimeOffset? TokenExpiresAt);

public record GrantAccountRequest([Required] Guid AccountId, bool IsDefault);

public enum GrantStatus { Granted = 0, SubscriberNotFound, AccountNotFound }

public sealed record GrantResult(GrantStatus Status, GrantedAccountSummary? Grant);
public record BatchStatusNotification(long BankBatchId, string BatchId, string BatchDetailId, string Status,
    string? DescriptionStatus, long TransactionIdDebit, string TransactionIdCredit);
