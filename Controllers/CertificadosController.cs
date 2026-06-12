using CENS15_V2.Models.DTOs.CertificadoTemplatesDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    //[Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CertificadosController : ControllerBase
    {
        private readonly ICertificadoTemplateService _service;
        private readonly IWebHostEnvironment _env;

        public CertificadosController(ICertificadoTemplateService service, IWebHostEnvironment env)
        {
            _service = service;
            _env = env;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpGet("{id:int}/render/alumno/{alumnoId:int}")]
        public async Task<IActionResult> RenderForAlumno(int id, int alumnoId, [FromQuery] string? inscripcionIds = null)
        {
            var ids = ParseInscripcionIds(inscripcionIds);
            var rendered = await _service.RenderForAlumnoAsync(id, alumnoId, ids);
            return rendered == null ? NotFound() : Ok(rendered);
        }

        private static IReadOnlyCollection<int>? ParseInscripcionIds(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var ids = value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => int.TryParse(v, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToArray();

            return ids.Length == 0 ? null : ids;
        }

        [HttpPost]
        public async Task<IActionResult> Post(CreateCertificadoTemplateRequest request)
        {
            try
            {
                var created = await _service.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Put(int id, UpdateCertificadoTemplateRequest request)
        {
            try
            {
                var ok = await _service.UpdateAsync(id, request);
                return ok ? NoContent() : NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var ok = await _service.DeleteAsync(id);
                return ok ? NoContent() : NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("subir-imagen")]
        public async Task<IActionResult> SubirImagen(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No se recibió ninguna imagen.");
            }

            var uploadsDir = Path.Combine(
                _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"),
                "uploads", "certificados");
            Directory.CreateDirectory(uploadsDir);

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg" };
            if (!allowed.Contains(ext))
            {
                return BadRequest($"Formato no permitido. Usar: {string.Join(", ", allowed)}");
            }

            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var url = $"{Request.Scheme}://{Request.Host}/uploads/certificados/{fileName}";
            return Ok(new { url, fileName });
        }
    }
}
