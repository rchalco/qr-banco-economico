using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QrBancoEconomico.Application;
using QrBancoEconomico.Domain;
using QrBancoEconomico.Infrastructure;

namespace QrBancoEconomico.Api.Controllers;

/// <summary>
/// Superficie que el proxy expone a sus suscriptores: emitir un QR y listar los QR cobrados de un día.
/// Nada más. El resto de la API de Baneco (cifrado, autenticación, anulación, estado, movimientos de
/// cuenta y planillas) queda deliberadamente fuera; en particular, exponer el descifrado obligaría a
/// aceptar la clave AES del banco por la red.
/// </summary>
[ApiController]
[Route("api/baneco")]
public sealed class BanecoController(
    IBanecoGateway gateway,
    BanecoDbContext db,
    ISubscriberContext subscriber,
    IBanecoAccountResolver accountResolver,
    IBanecoAccountContext accountContext,
    ISecretProtector protector) : ControllerBase
{
    /// <summary>Cabecera con la que el suscriptor elige entre las cuentas de Baneco que tiene concedidas.</summary>
    public const string AccountHeader = "X-Baneco-Account";

    [HttpPost("qrs")]
    [Authorize(Policy = ApiScopes.QrWrite)]
    public async Task<ActionResult<GenerateQrResponse>> GenerateQr(GenerateQrRequest request, CancellationToken ct)
    {
        var (account, resolutionError) = await ResolveAccountAsync(ct);
        if (resolutionError is not null) return resolutionError;

        var subscriberId = subscriber.SubscriberId;
        // La cuenta a acreditar es la del suscriptor y solo puede venir de aquí. Si llegara por el cuerpo,
        // conocer el criptograma de otro suscriptor bastaría para acreditarle cobros ajenos.
        var accountCredit = await accountResolver.ResolveAccountCreditAsync(subscriberId, ct);
        if (string.IsNullOrWhiteSpace(accountCredit))
            return Conflict(new ApiResult(1,
                $"El suscriptor '{subscriber.Code}' no tiene cuenta de ahorro asociada; cárguela con " +
                "PATCH /api/admin/subscribers/{id} antes de emitir QR."));

        if (await db.QrTransactions.AnyAsync(x => x.SubscriberId == subscriberId && x.MerchantTransactionId == request.TransactionId, ct))
            return Conflict("transactionId ya existe.");
        // Baneco exige transactionId único por cuenta: con una cuenta compartida entre suscriptores,
        // la colisión se detecta aquí en lugar de llegar como rechazo del banco.
        if (account is not null && await db.QrTransactions.AnyAsync(x => x.BanecoAccountId == account.Id && x.MerchantTransactionId == request.TransactionId, ct))
            return Conflict($"transactionId ya fue usado en la cuenta '{account.Code}' por otro suscriptor.");

        var response = await gateway.GenerateQrAsync(new BanecoGenerateQrRequest(request, accountCredit), ct);
        if (response.ResponseCode != 0 || string.IsNullOrWhiteSpace(response.QrImage) || response.QrId is null)
            return StatusCode(StatusCodes.Status502BadGateway, response);

        // Se guarda con la capa de la llave maestra, igual que en Subscribers: el registro histórico no
        // debe dejar el criptograma de la cuenta a la vista de quien lea la tabla.
        db.QrTransactions.Add(new QrTransaction { SubscriberId = subscriberId, BanecoAccountId = account?.Id, MerchantTransactionId = request.TransactionId, BankQrId = response.QrId, AccountCreditEncrypted = protector.Protect(accountCredit), Currency = request.Currency, Amount = request.Amount, Description = request.Description, DueDate = request.DueDate, SingleUse = request.SingleUse, ModifyAmount = request.ModifyAmount, BranchCode = request.BranchCode, QrImageBase64 = response.QrImage });
        await db.SaveChangesAsync(ct);
        return Ok(response);
    }

    [HttpGet("qrs/paid/{date:datetime}")]
    [Authorize(Policy = ApiScopes.QrRead)]
    public async Task<ActionResult<IReadOnlyList<PaymentQr>>> PaidQrs(DateTime date, CancellationToken ct)
    {
        var (_, resolutionError) = await ResolveAccountAsync(ct);
        if (resolutionError is not null) return resolutionError;

        // Baneco responde con los cobros de toda la cuenta, que puede atender a varios suscriptores;
        // se filtran a los QR emitidos por este para no exponer la operativa de un canal a otro.
        var paid = await gateway.GetPaidQrsAsync(DateOnly.FromDateTime(date), ct);
        if (paid.Count == 0) return Ok(paid);

        var subscriberId = subscriber.SubscriberId;
        var bankQrIds = paid.Select(p => p.QrId).Distinct().ToArray();
        var owned = await db.QrTransactions.AsNoTracking()
            .Where(x => x.SubscriberId == subscriberId && x.BankQrId != null && bankQrIds.Contains(x.BankQrId))
            .Select(x => x.BankQrId!)
            .ToListAsync(ct);
        var ownedSet = owned.ToHashSet(StringComparer.Ordinal);
        return Ok(paid.Where(p => ownedSet.Contains(p.QrId)).ToList());
    }

    /// <summary>
    /// Resuelve la cuenta de Baneco de la solicitud y la publica para el proveedor de token.
    /// Devuelve <c>(null, null)</c> en modo pass-through: el suscriptor no tiene cuentas concedidas
    /// y aporta sus propias credenciales de banco.
    /// </summary>
    private async Task<(ResolvedBanecoAccount? Account, ActionResult? Error)> ResolveAccountAsync(CancellationToken ct)
    {
        var requestedCode = Request.Headers.TryGetValue(AccountHeader, out var header) ? header.ToString() : null;
        var resolution = await accountResolver.ResolveAsync(subscriber.SubscriberId, requestedCode, ct);

        return resolution.Status switch
        {
            BanecoAccountResolutionStatus.Resolved => (Publish(resolution.Account!), null),
            BanecoAccountResolutionStatus.PassThrough => (null, null),
            BanecoAccountResolutionStatus.NotGranted =>
                (null, StatusCode(StatusCodes.Status403Forbidden, new ApiResult(1, $"La cuenta '{requestedCode}' no está concedida a este suscriptor."))),
            BanecoAccountResolutionStatus.Ambiguous =>
                (null, BadRequest(new ApiResult(1, $"El suscriptor tiene varias cuentas y ninguna predeterminada; indique {AccountHeader}."))),
            BanecoAccountResolutionStatus.Inactive =>
                (null, Conflict(new ApiResult(1, "La cuenta de Baneco está dada de baja."))),
            _ => (null, StatusCode(StatusCodes.Status500InternalServerError, new ApiResult(1, "No se pudo resolver la cuenta de Baneco.")))
        };
    }

    private ResolvedBanecoAccount Publish(ResolvedBanecoAccount account)
    {
        accountContext.Set(account);
        return account;
    }
}
