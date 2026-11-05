using System.Text.Json;
using System.Security.Claims;
using CENS15_V2.Models.DTOs.Transferencias;
using CENS15_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers;

[ApiController]
[Route("api/transferencias")]
[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(TransferenciaService.MaxBytes)]
public sealed class TransferenciasController(TransferenciaService service) : ControllerBase
{
    [HttpGet("{seccion}/exportar")]
    public async Task<IActionResult> Exportar(string seccion, CancellationToken cancellationToken)
    {
        try
        {
            if (!Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
                return Forbid();
            var paquete = await service.ExportarAsync(seccion, cancellationToken, actorId);
            return File(JsonSerializer.SerializeToUtf8Bytes(paquete, TransferenciaService.JsonOptions),
                "application/json", $"cens15-{seccion}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("preview")]
    public Task<IActionResult> Preview(SolicitudTransferencia solicitud, CancellationToken cancellationToken)
        => Ejecutar(solicitud, false, cancellationToken);

    [HttpPost("importar")]
    public Task<IActionResult> Importar(SolicitudTransferencia solicitud, CancellationToken cancellationToken)
        => Ejecutar(solicitud, true, cancellationToken);

    private async Task<IActionResult> Ejecutar(SolicitudTransferencia solicitud, bool importar, CancellationToken ct)
    {
        try
        {
            if (solicitud == null) return BadRequest(new { message = "Debes indicar el paquete y el modo." });
            var paquete = TransferenciaService.LeerPaquete(solicitud.Paquete);
            if (!Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
                return Forbid();
            var reporte = await service.ProcesarAsync(paquete, solicitud.Modo, importar,
                solicitud.Huella, solicitud.Confirmacion, ct, actorId);
            return importar && !reporte.Importado ? Conflict(reporte) : Ok(reporte);
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
