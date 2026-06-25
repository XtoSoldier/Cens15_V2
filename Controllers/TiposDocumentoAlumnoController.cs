using CENS15_V2.Models.DTOs.TiposDocumentoAlumnoDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TiposDocumentoAlumnoController : ControllerBase
    {
        private readonly ITipoDocumentoAlumnoService _service;

        public TiposDocumentoAlumnoController(ITipoDocumentoAlumnoService service)
        {
            _service = service;
        }

        [HttpGet]
        [Authorize(Policy = ResponsibilityPolicies.TiposDocumentoAlumnoConsultar)]
        public async Task<IActionResult> Get()
        {
            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id:int}")]
        [Authorize(Policy = ResponsibilityPolicies.TiposDocumentoAlumnoConsultar)]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpPost]
        [Authorize(Policy = ResponsibilityPolicies.TiposDocumentoAlumnoCrear)]
        public async Task<IActionResult> Post(CreateTipoDocumentoAlumnoRequest request)
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
        [Authorize(Policy = ResponsibilityPolicies.TiposDocumentoAlumnoEditar)]
        public async Task<IActionResult> Put(int id, UpdateTipoDocumentoAlumnoRequest request)
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
        [Authorize(Policy = ResponsibilityPolicies.TiposDocumentoAlumnoEliminar)]
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
    }
}
