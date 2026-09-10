namespace QrBancoEconomico.Domain;

public sealed class QrTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SubscriberId { get; set; }
    public Subscriber? Subscriber { get; set; }
    /// <summary>Cuenta de Baneco que atendió la operación. Nulo en modo pass-through o en filas heredadas.</summary>
    public Guid? BanecoAccountId { get; set; }
    public BanecoAccount? BanecoAccount { get; set; }
    public required string MerchantTransactionId { get; set; }
    public string? BankQrId { get; set; }
    public required string AccountCreditEncrypted { get; set; }
    public required string Currency { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateOnly DueDate { get; set; }
    public bool SingleUse { get; set; }
    public bool ModifyAmount { get; set; }
    public string? BranchCode { get; set; }
    public QrStatus Status { get; set; } = QrStatus.Pending;
    public string? QrImageBase64 { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<QrPayment> Payments { get; set; } = [];
}

public enum QrStatus { Pending = 0, Paid = 1, Cancelled = 9 }

public sealed class QrPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QrTransactionId { get; set; }
    public QrTransaction? QrTransaction { get; set; }
    public required string QrId { get; set; }
    public string? TransactionId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public string? PaymentTime { get; set; }
    public required string Currency { get; set; }
    public decimal Amount { get; set; }
    public string? SenderBankCode { get; set; }
    public string? SenderName { get; set; }
    public string? SenderDocumentId { get; set; }
    public string? SenderAccount { get; set; }
    public string? Description { get; set; }
    public string? BranchCode { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BatchUpload
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SubscriberId { get; set; }
    public Subscriber? Subscriber { get; set; }
    /// <summary>Cuenta de Baneco que atendió la operación. Nulo en modo pass-through o en filas heredadas.</summary>
    public Guid? BanecoAccountId { get; set; }
    public BanecoAccount? BanecoAccount { get; set; }
    public required string BatchId { get; set; }
    public required string Type { get; set; }
    public required string Description { get; set; }
    public bool DetailedDebit { get; set; }
    public required string AccountCodeEncrypted { get; set; }
    public required string Currency { get; set; }
    public decimal Amount { get; set; }
    public int PaymentCount { get; set; }
    public long? BankBatchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Consumidor autorizado de la API (canal, aplicación o entidad a la que se distribuye el servicio).</summary>
public sealed class Subscriber
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? ContactEmail { get; set; }
    /// <summary>
    /// Cuenta de ahorro que se acredita cuando se paga un QR de este suscriptor. Se guarda cifrada con
    /// la llave maestra sobre el criptograma que ya produjo la clave AES de Baneco, y nunca se devuelve
    /// por la API. Es la única fuente de <c>accountCredit</c>: el proxy no lo acepta en la solicitud.
    /// </summary>
    public string? SavingsAccountEncrypted { get; set; }
    public bool IsActive { get; set; } = true;
    public int RequestsPerMinute { get; set; } = 120;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<SubscriberApiKey> ApiKeys { get; set; } = [];
    public List<SubscriberBanecoAccount> Accounts { get; set; } = [];
}

/// <summary>
/// Credencial emitida a un suscriptor. La clave es un GUID que se guarda **en claro**, de modo que
/// pueda cargarse directamente en la base con un <c>INSERT</c> sin pasar por la API.
/// </summary>
/// <remarks>
/// Es una concesión deliberada a la operación, con un costo de seguridad concreto: quien obtenga una
/// copia de esta tabla —un respaldo, un volcado, una inyección SQL en cualquier otro punto— obtiene
/// credenciales funcionales de todos los suscriptores. El diseño anterior guardaba solo el SHA-256 y
/// una filtración no servía de nada. Compénselo restringiendo el acceso a la tabla, cifrando los
/// respaldos y rotando con <see cref="RevokedAt"/> ante cualquier sospecha.
/// El GUID debe generarse al azar (<c>NEWID()</c> o <c>Guid.NewGuid()</c>, 122 bits): nunca con
/// <c>NEWSEQUENTIALID()</c>, que produce valores contiguos y por tanto adivinables.
/// </remarks>
public sealed class SubscriberApiKey
{
    /// <summary>Identificador interno de la fila. Es el que aparece en los logs; no sirve para autenticarse.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Subscriber? Subscriber { get; set; }
    /// <summary>La clave que el suscriptor presenta en <c>X-Api-Key</c>. Nunca debe registrarse en un log.</summary>
    public Guid ApiKey { get; set; } = Guid.NewGuid();
    /// <summary>Ambiente para el que se emitió (dev/stg/prd). Impide usar una clave fuera de su ambiente.</summary>
    public required string Environment { get; set; }
    /// <summary>Alcances concedidos, separados por espacio.</summary>
    public required string Scopes { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>
/// Cuenta de Baneco que el proxy puede operar. Es un plano de credenciales distinto al de las API Keys:
/// la API Key identifica al suscriptor ante este proxy, esta entidad identifica la cuenta bancaria
/// contra la que el proxy opera. Las credenciales de banco se guardan aquí cifradas con la llave
/// maestra (medida transitoria hasta habilitar una bóveda); el token nunca se persiste, se obtiene
/// y se renueva en memoria. <see cref="CredentialRef"/> se conserva como respaldo de configuración.
/// </summary>
public sealed class BanecoAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>Nombre de la entrada de configuración con las credenciales: <c>Baneco:Accounts:{CredentialRef}</c>.</summary>
    public string? CredentialRef { get; set; }
    /// <summary>Usuario de Baneco, cifrado con la llave maestra. Nunca se devuelve por la API.</summary>
    public string? AuthUserNameEncrypted { get; set; }
    /// <summary>Contraseña de Baneco, cifrada con la llave maestra. Nunca se devuelve por la API ni se registra en logs.</summary>
    public string? AuthPasswordEncrypted { get; set; }
    /// <summary>Última autenticación exitosa contra el banco. Solo para diagnóstico; no guarda el token.</summary>
    public DateTimeOffset? CredentialsVerifiedAt { get; set; }
    /// <summary>
    /// Cuenta operativa cifrada con la clave AES del banco. <b>Ya no la usa ningún flujo:</b> la cuenta a
    /// acreditar pasó a ser <see cref="Subscriber.SavingsAccountEncrypted"/>, para que cada suscripción
    /// acredite la suya aunque varias compartan la misma credencial de banco. Se conserva por los datos
    /// existentes; puede eliminarse en una migración posterior.
    /// </summary>
    public string? AccountCodeEncrypted { get; set; }
    /// <summary>Cuenta de débito cifrada para planillas. Sin uso desde que se retiró el flujo de planillas.</summary>
    public string? BatchDebitAccountEncrypted { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<SubscriberBanecoAccount> Subscribers { get; set; } = [];
}

/// <summary>
/// Concesión N-N entre suscriptores y cuentas de Baneco: un suscriptor puede operar varias cuentas
/// y una cuenta puede atender a varios suscriptores (modo multiplexor).
/// </summary>
public sealed class SubscriberBanecoAccount
{
    public Guid SubscriberId { get; set; }
    public Subscriber? Subscriber { get; set; }
    public Guid BanecoAccountId { get; set; }
    public BanecoAccount? BanecoAccount { get; set; }
    /// <summary>Cuenta usada cuando la solicitud no indica una explícitamente. Como máximo una por suscriptor.</summary>
    public bool IsDefault { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Código del suscriptor administrador que concedió el acceso.</summary>
    public string? GrantedBy { get; set; }
}
