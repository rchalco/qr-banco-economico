using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using QrBancoEconomico.Application;

namespace QrBancoEconomico.Api.Security;

/// <summary>
/// Traduce un secreto ilegible a un 409 con instrucciones, en vez del 500 genérico.
/// </summary>
/// <remarks>
/// Es un fallo de aprovisionamiento, no de la solicitud: alguien cargó el dato con un <c>INSERT</c> sin
/// pasar por la API —y quedó sin cifrar— o la llave maestra ya no es la que lo cifró. Sin esto, el
/// operador ve «An error occurred while processing your request» y tiene que ir al log a descubrirlo.
/// El detalle no revela ningún valor: solo qué columna arreglar y cómo.
/// </remarks>
public sealed class ProtectedSecretExceptionHandler(ILogger<ProtectedSecretExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not ProtectedSecretException) return false;

        logger.LogError(exception, "Un secreto almacenado no se pudo descifrar al atender {RequestPath}.",
            context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "https://httpstatuses.io/409",
            title = "Secreto almacenado ilegible",
            status = StatusCodes.Status409Conflict,
            detail = $"{exception.Message} Recárguelo por la API —PATCH /api/admin/subscribers/{{id}} para la " +
                     "cuenta de ahorro, PATCH /api/admin/baneco-accounts/{id} para las credenciales del banco— " +
                     "en lugar de con un INSERT, que no cifra.",
            traceId = context.TraceIdentifier
        }), ct);
        return true;
    }
}
