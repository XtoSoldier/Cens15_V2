using CENS15_V2.Models;
using CENS15_V2.Services;
using CENS15_V2.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CENS15_V2.Controllers;

[ApiController]
[Route("api/checkpoints")]
[Authorize(Policy = ResponsibilityPolicies.AdminAccess)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[CheckpointErrors]
public sealed class CheckpointsController(CheckpointService checkpoints) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await checkpoints.ListAsync(User, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CheckpointCreate request, CancellationToken ct) => Ok(await checkpoints.CreateAsync(request, User, ct));

    [HttpPost("importar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(CheckpointStorage.MaximumBytes + 16384)]
    [RequestFormLimits(MultipartBodyLengthLimit = CheckpointStorage.MaximumBytes, ValueLengthLimit = 2048)]
    public async Task<IActionResult> Import(IFormFile archivo, [FromForm] string clave, CancellationToken ct)
    {
        if (archivo is null || archivo.Length < 52 || archivo.Length > CheckpointStorage.MaximumBytes ||
            !Path.GetExtension(archivo.FileName).Equals(".censbackup", StringComparison.OrdinalIgnoreCase))
            throw new CheckpointException("Seleccione un archivo .censbackup valido de hasta 128 MiB.");
        await using var source = archivo.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + read > CheckpointStorage.MaximumBytes) throw new CheckpointException("Archivo demasiado grande.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        return Ok(await checkpoints.ImportAsync(buffer.ToArray(), clave, User, ct));
    }

    [HttpPost("{id:guid}/exportar")]
    public async Task<IActionResult> Export(Guid id, [FromBody] CheckpointExport request, CancellationToken ct) =>
        File(await checkpoints.ExportAsync(id, request.Clave, User, ct), "application/octet-stream", $"checkpoint-{id:D}.censbackup");

    [HttpPost("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct) => Ok(await checkpoints.PreviewAsync(id, User, ct));

    [HttpPost("{id:guid}/restaurar")]
    public async Task<IActionResult> Restore(Guid id, [FromBody] CheckpointRestore request, CancellationToken ct) => Ok(await checkpoints.RestoreAsync(id, request, User, ct));
}

public sealed class CheckpointErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        var exception = context.Exception;
        var status = exception is CheckpointException expected ? expected.Status : 409;
        var message = exception is CheckpointException known ? known.Message :
            "No se pudo completar el checkpoint. Verifique el almacenamiento y la compatibilidad; si existe recuperacion pendiente el servicio permanecera bloqueado.";
        // Never expose EF values, paths, hashes, passwords or database exception details in a response.
        context.Result = new ObjectResult(new { message }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
