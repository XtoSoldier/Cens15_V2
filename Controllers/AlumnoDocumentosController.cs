using CENS15_V2.Models.DTOs.AlumnoDocumentosDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
        [Authorize(Policy = ResponsibilityPolicies.AlumnoDocumentosConsultar)]
        public async Task<IActionResult> Get([FromQuery] int? alumnoId)
        {
            return Ok(await _service.GetAllAsync(alumnoId));
        }

        [HttpGet("{id:int}")]
        [Authorize(Policy = ResponsibilityPolicies.AlumnoDocumentosConsultar)]
        public async Task<IActionResult> GetById(int id)
        {
            var documento = await _service.GetByIdAsync(id);
            return documento == null ? NotFound() : Ok(documento);
        }

        [HttpPost]
        [Authorize(Policy = ResponsibilityPolicies.AlumnoDocumentosCrear)]
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

        [HttpPost("upload")]
        [Authorize(Policy = ResponsibilityPolicies.AlumnoDocumentosCrear)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> PostUpload([FromForm] CreateAlumnoDocumentoItemRequest request)
        {
            return await Post(request);
        }

        [HttpPut("{id:int}")]
        [Authorize(Policy = ResponsibilityPolicies.AlumnoDocumentosEditar)]
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

        [HttpPut("{id:int}/upload")]
        [Authorize(Policy = ResponsibilityPolicies.AlumnoDocumentosEditar)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> PutUpload(int id, [FromForm] UpdateAlumnoDocumentoItemRequest request)
        {
            return await Put(id, request);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Policy = ResponsibilityPolicies.AlumnoDocumentosEliminar)]
        public async Task<IActionResult> Delete(int id)
        {
            var ok = await _service.DeleteAsync(id);
            return ok ? NoContent() : NotFound();
        }
    }
}
