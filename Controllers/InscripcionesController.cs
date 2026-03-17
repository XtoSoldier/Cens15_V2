using CENS15_V2.Models.DTOs.InscripcionesDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InscripcionesController : ControllerBase
    {
        private readonly IInscripcionService _service;

        public InscripcionesController(IInscripcionService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<IActionResult> Post(CreateInscripcionRequest request)
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

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpGet("/api/alumnos/{id:int}/inscripciones")]
        public async Task<IActionResult> GetByAlumno(int id)
        {
            return Ok(await _service.GetByAlumnoIdAsync(id));
        }

        [HttpGet("/api/cursos/{id:int}/inscripciones")]
        public async Task<IActionResult> GetByCurso(int id, [FromQuery] int? anio)
        {
            return Ok(await _service.GetByCursoIdAsync(id, anio));
        }

        [HttpPatch("{id:int}/anular")]
        public async Task<IActionResult> Anular(int id)
        {
            var ok = await _service.AnularAsync(id);
            return ok ? NoContent() : NotFound();
        }

        [HttpPut("{id:int}/estado")]
        public async Task<IActionResult> UpdateEstado(int id, UpdateInscripcionEstadoRequest request)
        {
            try
            {
                var ok = await _service.UpdateEstadoAsync(id, request.Estado);
                return ok ? NoContent() : NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
