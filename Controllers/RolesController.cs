using CENS15_V2.Models.DTOs.RolesDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RolesController : ControllerBase
    {
        private readonly IRoleService _service;

        public RolesController(IRoleService service)
        {
            _service = service;
        }

        [HttpGet]
        [Authorize(Policy = ResponsibilityPolicies.RolesConsultar)]
        public async Task<IActionResult> Get()
        {
            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id}")]
        [Authorize(Policy = ResponsibilityPolicies.RolesConsultar)]
        public async Task<IActionResult> Get(Guid id)
        {
            var role = await _service.GetByIdAsync(id);
            return role == null ? NotFound() : Ok(role);
        }

        [HttpGet("{id}/responsibilities")]
        [Authorize(Policy = ResponsibilityPolicies.RolesConsultar)]
        public async Task<IActionResult> GetResponsibilities(Guid id)
        {
            var role = await _service.GetByIdAsync(id);
            if (role == null)
            {
                return NotFound();
            }

            var responsibilities = await _service.GetResponsibilitiesAsync(id);
            return Ok(responsibilities);
        }

        [HttpPost]
        [Authorize(Policy = ResponsibilityPolicies.RolesCrear)]
        public async Task<IActionResult> Post(CreateRoleRequest request)
        {
            try
            {
                var created = await _service.CreateAsync(request);
                return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id}")]
        [Authorize(Policy = ResponsibilityPolicies.RolesEditar)]
        public async Task<IActionResult> Put(Guid id, UpdateRoleRequest request)
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

        [HttpPut("{id}/responsibilities")]
        [Authorize(Policy = ResponsibilityPolicies.RolesEditar)]
        public async Task<IActionResult> AssignResponsibilities(Guid id, AssignResponsibilitiesRequest request)
        {
            try
            {
                var ok = await _service.AssignResponsibilitiesAsync(id, request);
                return ok ? NoContent() : NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = ResponsibilityPolicies.RolesEliminar)]
        public async Task<IActionResult> Delete(Guid id)
        {
            var ok = await _service.DeleteAsync(id);
            return ok ? NoContent() : NotFound();
        }
    }
}
