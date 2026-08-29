namespace QrBancoEconomico.Domain;

public sealed class QrTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
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
