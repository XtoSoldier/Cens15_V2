using CENS15_V2.Models.DTOs.CursadasMateriasDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CursadasMateriasController : ControllerBase
    {
        private readonly ICursadaMateriaService _service;

        public CursadasMateriasController(ICursadaMateriaService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<IActionResult> Post(CreateCursadaMateriaRequest request)
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
        public async Task<IActionResult> Put(int id, UpdateCursadaMateriaRequest request)
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

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpGet("/api/inscripciones/{inscripcionId:int}/cursadas-materias")]
        public async Task<IActionResult> GetByInscripcion(int inscripcionId)
        {
            return Ok(await _service.GetByInscripcionIdAsync(inscripcionId));
        }
    }
}
