using Microsoft.AspNetCore.Mvc;
using QrBancoEconomico.Infrastructure;

namespace QrBancoEconomico.Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController(BanecoDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct) ? Ok(new { status = "ok" }) : StatusCode(503, new { status = "database-unavailable" });
}
