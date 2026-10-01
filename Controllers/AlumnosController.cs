using CENS15_V2.Models.DTOs.AlumnosDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

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
        public async Task<IActionResult> Get([FromQuery] bool includeDeleted = false)
        {
            if (includeDeleted && !IsAdministrator())
            {
                return Forbid();
            }

            return Ok(await _service.GetAllAsync(includeDeleted));
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
        [Authorize]
        public async Task<IActionResult> Delete(int id)
        {
            if (!IsAdministrator()) return Forbid();

            var ok = await _service.DeleteAsync(id);
            return ok ? NoContent() : NotFound();
        }

        [HttpGet("{id:int}/deletion-info")]
        [Authorize]
        public async Task<IActionResult> GetDeletionInfo(int id)
        {
            if (!IsAdministrator()) return Forbid();

            var info = await _service.GetDeletionInfoAsync(id);
            return info == null ? NotFound() : Ok(info);
        }

        [HttpPatch("{id:int}/restore")]
        [Authorize]
        public async Task<IActionResult> Restore(int id)
        {
            if (!IsAdministrator()) return Forbid();

            var ok = await _service.RestoreAsync(id);
            return ok ? NoContent() : NotFound();
        }

        private bool IsAdministrator()
        {
            return User.FindAll(ClaimTypes.Role)
                .Select(claim => claim.Value.Replace(" ", string.Empty))
                .Any(role => role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase));
        }
    }
}
