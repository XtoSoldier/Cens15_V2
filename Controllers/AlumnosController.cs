using CENS15_V2.Models.DTOs.AlumnosDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AlumnosController : ControllerBase
    {
        private readonly IAlumnoService _service;

        public AlumnosController(IAlumnoService service)
        {
            _service = service;
        }

        [HttpGet]
        [Authorize(Policy = ResponsibilityPolicies.AlumnosConsultar)]
        public async Task<IActionResult> Get()
        {
            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id:int}")]
        [Authorize(Policy = ResponsibilityPolicies.AlumnosConsultar)]
        public async Task<IActionResult> GetById(int id)
        {
            var alumno = await _service.GetByIdAsync(id);
            return alumno == null ? NotFound() : Ok(alumno);
        }

        [HttpPost]
        [Authorize(Policy = ResponsibilityPolicies.AlumnosCrear)]
        public async Task<IActionResult> Post(CreateAlumnoRequest request)
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
        [Authorize(Policy = ResponsibilityPolicies.AlumnosEditar)]
        public async Task<IActionResult> Put(int id, UpdateAlumnoRequest request)
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
        [Authorize(Policy = ResponsibilityPolicies.AlumnosEliminar)]
        public async Task<IActionResult> Delete(int id)
        {
            var ok = await _service.DeleteAsync(id);
            return ok ? NoContent() : NotFound();
        }
    }
}
