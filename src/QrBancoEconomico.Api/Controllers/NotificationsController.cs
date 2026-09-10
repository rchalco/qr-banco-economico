using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QrBancoEconomico.Application;
using QrBancoEconomico.Domain;
using QrBancoEconomico.Infrastructure;

namespace QrBancoEconomico.Api.Controllers;

/// <summary>
/// Callbacks del banco. Requieren una API Key propia con alcance <c>notifications:write</c>,
/// emitida al suscriptor que representa a Baneco y distinta de las claves de los canales consumidores.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize(Policy = ApiScopes.NotificationsWrite)]
public sealed class NotificationsController(BanecoDbContext db) : ControllerBase
{
    [HttpPost("qr-payments")]
    public async Task<ActionResult<ApiResult>> QrPayment(PaymentNotification notification, CancellationToken ct)
    {
        var payment = notification.Payment;
        if (await db.QrPayments.AsNoTracking().AnyAsync(x => x.QrId == payment.QrId && x.TransactionId == payment.TransactionId, ct))
            return Ok(new ApiResult(0, "Notificación ya procesada."));

        var qrTransactionId = await db.QrTransactions
            .Where(x => x.BankQrId == payment.QrId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(ct);
        if (qrTransactionId is null) return NotFound(new ApiResult(1, "QR no registrado."));

        var updatedRows = await db.QrTransactions
            .Where(x => x.Id == qrTransactionId.Value)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, QrStatus.Paid), ct);
        if (updatedRows == 0)
            return Conflict(new ApiResult(1, "El QR cambió durante el procesamiento; reintente la notificación."));

        db.QrPayments.Add(new QrPayment { QrTransactionId = qrTransactionId.Value, QrId = payment.QrId, TransactionId = payment.TransactionId, PaymentDate = payment.PaymentDate, PaymentTime = payment.PaymentTime, Currency = payment.Currency, Amount = payment.Amount, SenderBankCode = payment.SenderBankCode, SenderName = payment.SenderName, SenderDocumentId = payment.SenderDocumentId, SenderAccount = payment.SenderAccount, Description = payment.Description, BranchCode = payment.BranchCode });
        await db.SaveChangesAsync(ct);
        return Ok(new ApiResult(0, "OK"));
    }

    [HttpPost("batch-status")]
    public ActionResult<ApiResult> BatchStatus(BatchStatusNotification notification) => Ok(new ApiResult(0, "OK"));
}
