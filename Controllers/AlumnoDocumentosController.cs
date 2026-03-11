using CENS15_V2.Models.DTOs.AlumnoDocumentosDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AlumnoDocumentosController : ControllerBase
    {
        private readonly IAlumnoDocumentoService _service;

        public AlumnoDocumentosController(IAlumnoDocumentoService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] int? alumnoId)
        {
            return Ok(await _service.GetAllAsync(alumnoId));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var documento = await _service.GetByIdAsync(id);
            return documento == null ? NotFound() : Ok(documento);
        }

        [HttpPost]
        public async Task<IActionResult> Post(CreateAlumnoDocumentoItemRequest request)
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
        public async Task<IActionResult> Put(int id, UpdateAlumnoDocumentoItemRequest request)
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
            var ok = await _service.DeleteAsync(id);
            return ok ? NoContent() : NotFound();
        }
    }
}
