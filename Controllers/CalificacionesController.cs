using CENS15_V2.Models.DTOs.CalificacionesDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CalificacionesController : ControllerBase
    {
        private readonly ICalificacionService _service;

        public CalificacionesController(ICalificacionService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<IActionResult> Post(CreateCalificacionRequest request)
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
        public async Task<IActionResult> Put(int id, UpdateCalificacionRequest request)
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

        [HttpGet("/api/cursadas-materias/{cursadaMateriaId:int}/calificacion")]
        public async Task<IActionResult> GetByCursadaMateria(int cursadaMateriaId)
        {
            var item = await _service.GetByCursadaMateriaIdAsync(cursadaMateriaId);
            return item == null ? NotFound() : Ok(item);
        }
    }
}
