using System.ComponentModel.DataAnnotations;

namespace QrBancoEconomico.Application;

public record ApiResult(int ResponseCode, string Message);
public record GenerateQrRequest(
    [Required] string TransactionId, [Required] string AccountCredit, [Required] string Currency,
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
public record GenerateQrResponse(int ResponseCode, string Message, string? QrId, string? QrImage);
public record PaymentQr(string QrId, string? TransactionId, DateOnly PaymentDate, string? PaymentTime,
    string Currency, decimal Amount, string? SenderBankCode, string? SenderName, string? SenderDocumentId,
    string? SenderAccount, string? Description, string? BranchCode);
public record QrStatusResponse(int ResponseCode, string Message, int StatusQrCode, IReadOnlyList<PaymentQr> Payment);
public record AccountHistoryRequest([Required] string AccountCode, DateOnly StartDate, DateOnly EndDate);
public record BatchPaymentItem([Required] string BatchDetailId, [Range(typeof(decimal), "0.01", "5000.00", ParseLimitsInInvariantCulture = true)] decimal Amount,
    [Required] string AccountCode, [Required] string AccountTypeCode, [Required] string BankCode,
    [Required] string BeneficiaryName, string? BeneficiaryDocId, string? BeneficiaryPhone, string? BeneficiaryEmail,
    string? Note, string? AmlSource, string? AmlDestination);
public record BatchUploadRequest([Required] string BatchId, [Required] string Type, [Required] string Description,
    bool DetailedDebit, [Required] string AccountCode, [Required] string BatchCurrency,
    [Range(typeof(decimal), "0.01", "5000.00", ParseLimitsInInvariantCulture = true)] decimal BatchAmount, [Required] string AmlSource,
    [Required] string AmlDestination, [MinLength(1)] IReadOnlyList<BatchPaymentItem> PaymentList);
public record BatchUploadResponse(int ResponseCode, string Message, long? BankBatchId);
public record PaymentNotification(PaymentQr Payment);
public record BatchStatusNotification(long BankBatchId, string BatchId, string BatchDetailId, string Status,
    string? DescriptionStatus, long TransactionIdDebit, string TransactionIdCredit);
