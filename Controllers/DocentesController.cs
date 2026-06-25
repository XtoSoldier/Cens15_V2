using CENS15_V2.Models.DTOs.DocentesDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocentesController : ControllerBase
    {
        private readonly IDocenteService _service;

        public DocentesController(IDocenteService service)
        {
            _service = service;
        }

        [HttpGet]
        [Authorize(Policy = ResponsibilityPolicies.DocentesConsultar)]
        public async Task<IActionResult> Get()
        {
            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id:int}")]
        [Authorize(Policy = ResponsibilityPolicies.DocentesConsultar)]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpPost]
        [Authorize(Policy = ResponsibilityPolicies.DocentesCrear)]
        public async Task<IActionResult> Post(CreateDocenteRequest request)
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
        [Authorize(Policy = ResponsibilityPolicies.DocentesEditar)]
        public async Task<IActionResult> Put(int id, UpdateDocenteRequest request)
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
        [Authorize(Policy = ResponsibilityPolicies.DocentesEliminar)]
        public async Task<IActionResult> Delete(int id)
        {
            var ok = await _service.DeleteAsync(id);
            return ok ? NoContent() : NotFound();
        }

        [HttpGet("por-usuario/{userId:guid}")]
        [Authorize(Policy = ResponsibilityPolicies.DocentesConsultar)]
        public async Task<IActionResult> GetByUserId(Guid userId)
        {
            var item = await _service.GetByUserIdAsync(userId);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpGet("{id:int}/alumnos-para-calificar")]
        [Authorize(Policy = ResponsibilityPolicies.DocentesConsultar)]
        public async Task<IActionResult> GetAlumnosParaCalificar(int id)
        {
            return Ok(await _service.GetMateriasConAlumnosAsync(id));
        }

        [Authorize(Policy = ResponsibilityPolicies.CalificacionesConsultar)]
        [HttpGet("mis-alumnos-para-calificar")]
        public async Task<IActionResult> GetMisAlumnosParaCalificar()
        {
            var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (!Guid.TryParse(userIdValue, out var userId))
            {
                return Unauthorized();
            }

            return Ok(await _service.GetMateriasActivasConAlumnosByUserIdAsync(userId));
        }
    }
}
