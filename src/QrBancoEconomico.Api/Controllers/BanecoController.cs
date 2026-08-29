using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QrBancoEconomico.Application;
using QrBancoEconomico.Domain;
using QrBancoEconomico.Infrastructure;

namespace QrBancoEconomico.Api.Controllers;

[ApiController]
[Route("api/baneco")]
public sealed class BanecoController(IBanecoGateway gateway, BanecoDbContext db) : ControllerBase
{
    [HttpGet("encryption/encrypt")]
    public async Task<ActionResult<string>> Encrypt([FromQuery] string text, [FromQuery] string aesKey, CancellationToken ct) => Ok(await gateway.EncryptAsync(text, aesKey, ct));

    [HttpGet("encryption/decrypt")]
    public async Task<ActionResult<string>> Decrypt([FromQuery] string text, [FromQuery] string aesKey, CancellationToken ct) => Ok(await gateway.DecryptAsync(text, aesKey, ct));

    [HttpPost("authentication")]
    public async Task<ActionResult<object>> Authenticate([FromBody] AuthenticationRequest request, CancellationToken ct) => Ok(await gateway.AuthenticateAsync(request.UserName, request.Password, ct));

    [HttpPost("qrs")]
    public async Task<ActionResult<GenerateQrResponse>> GenerateQr(GenerateQrRequest request, CancellationToken ct)
    {
        if (!IsBase64(request.AccountCredit))
            return BadRequest("accountCredit debe enviarse cifrada y codificada en Base64.");
        if (await db.QrTransactions.AnyAsync(x => x.MerchantTransactionId == request.TransactionId, ct)) return Conflict("transactionId ya existe.");
        var response = await gateway.GenerateQrAsync(request, ct);
        if (response.ResponseCode != 0 || string.IsNullOrWhiteSpace(response.QrImage))
            return StatusCode(StatusCodes.Status502BadGateway, response);
        if (response.ResponseCode == 0 && response.QrId is not null)
        {
            db.QrTransactions.Add(new QrTransaction { MerchantTransactionId = request.TransactionId, BankQrId = response.QrId, AccountCreditEncrypted = request.AccountCredit, Currency = request.Currency, Amount = request.Amount, Description = request.Description, DueDate = request.DueDate, SingleUse = request.SingleUse, ModifyAmount = request.ModifyAmount, BranchCode = request.BranchCode, QrImageBase64 = response.QrImage });
            await db.SaveChangesAsync(ct);
        }
        return Ok(response);
    }

    private static bool IsBase64(string value) =>
        Convert.TryFromBase64String(value, new byte[value.Length], out _);

    [HttpDelete("qrs/{qrId}")]
    public async Task<ActionResult<ApiResult>> CancelQr(string qrId, CancellationToken ct)
    {
        var response = await gateway.CancelQrAsync(qrId, ct);
        var qr = await db.QrTransactions.SingleOrDefaultAsync(x => x.BankQrId == qrId, ct);
        if (response.ResponseCode == 0 && qr is not null) { qr.Status = QrStatus.Cancelled; await db.SaveChangesAsync(ct); }
        return Ok(response);
    }

    [HttpGet("qrs/{qrId}")]
    public async Task<ActionResult<QrStatusResponse>> StatusQr(string qrId, CancellationToken ct) => Ok(await gateway.GetQrStatusAsync(qrId, ct));

    [HttpGet("qrs/paid/{date:datetime}")]
    public async Task<ActionResult<IReadOnlyList<PaymentQr>>> PaidQrs(DateTime date, CancellationToken ct) => Ok(await gateway.GetPaidQrsAsync(DateOnly.FromDateTime(date), ct));

    [HttpPost("accounts/history")]
    public async Task<ActionResult<object>> AccountHistory(AccountHistoryRequest request, CancellationToken ct)
    {
        if (request.EndDate < request.StartDate) return BadRequest("endDate debe ser igual o posterior a startDate.");
        return Ok(await gateway.GetAccountHistoryAsync(request, ct));
    }

    [HttpPost("batches")]
    public async Task<ActionResult<BatchUploadResponse>> UploadBatch(BatchUploadRequest request, CancellationToken ct)
    {
        if (await db.BatchUploads.AnyAsync(x => x.BatchId == request.BatchId, ct)) return Conflict("batchId ya existe.");
        var response = await gateway.UploadBatchAsync(request, ct);
        if (response.ResponseCode == 0)
        {
            db.BatchUploads.Add(new BatchUpload { BatchId = request.BatchId, Type = request.Type, Description = request.Description, DetailedDebit = request.DetailedDebit, AccountCodeEncrypted = request.AccountCode, Currency = request.BatchCurrency, Amount = request.BatchAmount, PaymentCount = request.PaymentList.Count, BankBatchId = response.BankBatchId });
            await db.SaveChangesAsync(ct);
        }
        return Ok(response);
    }
}

public record AuthenticationRequest(string UserName, string Password);
