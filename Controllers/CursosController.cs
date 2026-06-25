using CENS15_V2.Models.DTOs.CursosDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CursosController : ControllerBase
    {
        private readonly ICursoService _service;

        public CursosController(ICursoService service)
        {
            _service = service;
        }

        [HttpGet]
        [Authorize(Policy = ResponsibilityPolicies.CursosConsultar)]
        public async Task<IActionResult> Get()
        {
            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id:int}")]
        [Authorize(Policy = ResponsibilityPolicies.CursosConsultar)]
        public async Task<IActionResult> GetById(int id)
        {
            var curso = await _service.GetByIdAsync(id);
            return curso == null ? NotFound() : Ok(curso);
        }

        [HttpPost]
        [Authorize(Policy = ResponsibilityPolicies.CursosCrear)]
        public async Task<IActionResult> Post(CreateCursoRequest request)
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
        [Authorize(Policy = ResponsibilityPolicies.CursosEditar)]
        public async Task<IActionResult> Put(int id, UpdateCursoRequest request)
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
        [Authorize(Policy = ResponsibilityPolicies.CursosEliminar)]
        public async Task<IActionResult> Delete(int id)
        {
            var ok = await _service.DeleteAsync(id);
            return ok ? NoContent() : NotFound();
        }
    }
}
