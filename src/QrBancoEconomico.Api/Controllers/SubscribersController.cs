using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QrBancoEconomico.Application;

namespace QrBancoEconomico.Api.Controllers;

/// <summary>
/// Distribución del servicio: alta de suscriptores y emisión, rotación y revocación de sus API Keys.
/// Exige el alcance <c>admin</c>, que no debe concederse a ninguna clave de canal.
/// </summary>
[ApiController]
[Route("api/admin/subscribers")]
[Authorize(Policy = ApiScopes.Admin)]
public sealed class SubscribersController(ISubscriberProvisioning provisioning, ILogger<SubscribersController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SubscriberSummary>>> List(CancellationToken ct) =>
        Ok(await provisioning.ListSubscribersAsync(ct));

    [HttpGet("{subscriberId:guid}")]
    public async Task<ActionResult<SubscriberSummary>> Get(Guid subscriberId, CancellationToken ct)
    {
        var subscriber = await provisioning.GetSubscriberAsync(subscriberId, ct);
        return subscriber is null ? NotFound() : Ok(subscriber);
    }

    [HttpPost]
    public async Task<ActionResult<SubscriberSummary>> Create(CreateSubscriberRequest request, CancellationToken ct)
    {
        var subscriber = await provisioning.CreateSubscriberAsync(request, ct);
        if (subscriber is null) return Conflict($"Ya existe un suscriptor con code '{request.Code}'.");

        logger.LogInformation("Suscriptor {SubscriberCode} creado por {Actor}.", subscriber.Code, User.Identity?.Name);
        return CreatedAtAction(nameof(Get), new { subscriberId = subscriber.Id }, subscriber);
    }

    [HttpPatch("{subscriberId:guid}")]
    public async Task<ActionResult<SubscriberSummary>> Update(Guid subscriberId, UpdateSubscriberRequest request, CancellationToken ct)
    {
        var subscriber = await provisioning.UpdateSubscriberAsync(subscriberId, request, ct);
        if (subscriber is null) return NotFound();

        logger.LogInformation("Suscriptor {SubscriberCode} actualizado por {Actor}; activo={IsActive}, rpm={RequestsPerMinute}.",
            subscriber.Code, User.Identity?.Name, subscriber.IsActive, subscriber.RequestsPerMinute);
        return Ok(subscriber);
    }

    /// <summary>
    /// Emite una clave. El GUID se devuelve en esta respuesta y además queda legible en
    /// <c>SubscriberApiKeys.ApiKey</c>, de donde puede volver a consultarse.
    /// </summary>
    [HttpPost("{subscriberId:guid}/keys")]
    public async Task<ActionResult<IssuedApiKey>> IssueKey(Guid subscriberId, IssueApiKeyRequest request, CancellationToken ct)
    {
        var unknownScopes = request.Scopes.Where(scope => !ApiScopes.All.Contains(scope)).ToArray();
        if (unknownScopes.Length > 0)
            return BadRequest($"Alcances no reconocidos: {string.Join(", ", unknownScopes)}. Válidos: {string.Join(", ", ApiScopes.All)}.");
        if (request.ExpiresAt is not null && request.ExpiresAt <= DateTimeOffset.UtcNow)
            return BadRequest("expiresAt debe ser posterior al momento actual.");

        var issued = await provisioning.IssueApiKeyAsync(subscriberId, request, ct);
        if (issued is null) return NotFound();

        // Se registra el identificador de la fila, nunca el GUID de la clave: ese es la credencial.
        logger.LogInformation("API Key {ApiKeyId} emitida para el suscriptor {SubscriberId} por {Actor} con alcances {Scopes}.",
            issued.Key.Id, subscriberId, User.Identity?.Name, issued.Key.Scopes);
        return Ok(issued);
    }

    [HttpDelete("{subscriberId:guid}/keys/{apiKeyId:guid}")]
    public async Task<IActionResult> RevokeKey(Guid subscriberId, Guid apiKeyId, CancellationToken ct)
    {
        if (!await provisioning.RevokeApiKeyAsync(subscriberId, apiKeyId, ct))
            return NotFound("La clave no existe para este suscriptor o ya estaba revocada.");

        logger.LogInformation("API Key {ApiKeyId} del suscriptor {SubscriberId} revocada por {Actor}.",
            apiKeyId, subscriberId, User.Identity?.Name);
        return NoContent();
    }
}
