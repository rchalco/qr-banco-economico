using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QrBancoEconomico.Application;

namespace QrBancoEconomico.Api.Controllers;

/// <summary>
/// Inventario de cuentas de Baneco que el proxy puede operar y su concesión a suscriptores (N-N):
/// un suscriptor puede operar varias cuentas y una cuenta puede atender a varios suscriptores.
/// Las credenciales de banco se cargan aquí una sola vez: se cifran con la llave maestra antes de
/// guardarse y no vuelven a salir por la API. El token lo obtiene y renueva el proxy por su cuenta.
/// </summary>
[ApiController]
[Route("api/admin/baneco-accounts")]
[Authorize(Policy = ApiScopes.Admin)]
public sealed class BanecoAccountsController(
    IBanecoAccountInventory inventory,
    IBanecoTokenService tokenService,
    ILogger<BanecoAccountsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BanecoAccountSummary>>> List(CancellationToken ct) =>
        Ok(await inventory.ListAccountsAsync(ct));

    [HttpGet("{accountId:guid}")]
    public async Task<ActionResult<BanecoAccountSummary>> Get(Guid accountId, CancellationToken ct)
    {
        var account = await inventory.GetAccountAsync(accountId, ct);
        return account is null ? NotFound() : Ok(account);
    }

    [HttpPost]
    public async Task<ActionResult<BanecoAccountSummary>> Create(CreateBanecoAccountRequest request, CancellationToken ct)
    {
        var account = await inventory.CreateAccountAsync(request, ct);
        if (account is null) return Conflict($"Ya existe una cuenta con code '{request.Code}'.");

        logger.LogInformation("Cuenta Baneco {AccountCode} registrada por {Actor}.", account.Code, User.Identity?.Name);
        return CreatedAtAction(nameof(Get), new { accountId = account.Id }, account);
    }

    [HttpPatch("{accountId:guid}")]
    public async Task<ActionResult<BanecoAccountSummary>> Update(Guid accountId, UpdateBanecoAccountRequest request, CancellationToken ct)
    {
        var account = await inventory.UpdateAccountAsync(accountId, request, ct);
        if (account is null) return NotFound();

        logger.LogInformation("Cuenta Baneco {AccountCode} actualizada por {Actor}; activa={IsActive}, credenciales={HasCredentials}.",
            account.Code, User.Identity?.Name, account.IsActive, account.HasCredentials);
        return Ok(account);
    }

    /// <summary>
    /// Prueba las credenciales contra Baneco y devuelve si la autenticación funciona. No devuelve el token.
    /// Sirve para validar el alta sin esperar a que falle una operación real.
    /// </summary>
    [HttpPost("{accountId:guid}/verify-credentials")]
    public async Task<ActionResult<BanecoCredentialCheck>> VerifyCredentials(Guid accountId, CancellationToken ct)
    {
        if (await inventory.GetAccountAsync(accountId, ct) is not { } account) return NotFound();

        var check = await tokenService.VerifyAsync(accountId, ct);
        logger.LogInformation("Credenciales de la cuenta {AccountCode} verificadas por {Actor}; resultado={Succeeded}.",
            account.Code, User.Identity?.Name, check.Succeeded);
        return check.Succeeded ? Ok(check) : StatusCode(StatusCodes.Status502BadGateway, check);
    }
}

/// <summary>Concesiones de cuentas de Baneco vistas desde el suscriptor: el otro extremo de la relación N-N.</summary>
[ApiController]
[Route("api/admin/subscribers/{subscriberId:guid}/accounts")]
[Authorize(Policy = ApiScopes.Admin)]
public sealed class SubscriberAccountsController(
    IBanecoAccountInventory inventory,
    IBanecoAccountResolver resolver,
    ILogger<SubscriberAccountsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GrantedAccountSummary>>> List(Guid subscriberId, CancellationToken ct) =>
        Ok(await resolver.ListGrantedAsync(subscriberId, ct));

    /// <summary>Concede una cuenta al suscriptor, o actualiza si ya estaba concedida.</summary>
    [HttpPost]
    public async Task<ActionResult<GrantedAccountSummary>> Grant(Guid subscriberId, GrantAccountRequest request, CancellationToken ct)
    {
        var result = await inventory.GrantAsync(subscriberId, request, User.Identity?.Name, ct);
        switch (result.Status)
        {
            case GrantStatus.SubscriberNotFound:
                return NotFound("El suscriptor no existe.");
            case GrantStatus.AccountNotFound:
                return NotFound("La cuenta de Baneco no existe.");
            default:
                logger.LogInformation("Cuenta {AccountCode} concedida al suscriptor {SubscriberId} por {Actor}; predeterminada={IsDefault}.",
                    result.Grant!.Code, subscriberId, User.Identity?.Name, result.Grant.IsDefault);
                return Ok(result.Grant);
        }
    }

    [HttpDelete("{accountId:guid}")]
    public async Task<IActionResult> Revoke(Guid subscriberId, Guid accountId, CancellationToken ct)
    {
        if (!await inventory.RevokeGrantAsync(subscriberId, accountId, ct))
            return NotFound("La cuenta no estaba concedida a este suscriptor.");

        logger.LogInformation("Concesión de la cuenta {AccountId} al suscriptor {SubscriberId} revocada por {Actor}.",
            accountId, subscriberId, User.Identity?.Name);
        return NoContent();
    }
}
