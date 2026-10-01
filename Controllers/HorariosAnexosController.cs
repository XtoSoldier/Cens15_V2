using CENS15_V2.Models.DTOs.HorariosAnexosDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/anexos/{anexoId:int}/horarios")]
    public class HorariosAnexosController : ControllerBase
    {
        private readonly IHorarioAnexoService _service;

        public HorariosAnexosController(IHorarioAnexoService service)
        {
            _service = service;
        }

        [HttpGet]
        [Authorize(Policy = ResponsibilityPolicies.AnexosConsultar)]
        public async Task<IActionResult> Get(int anexoId)
        {
            var horarios = await _service.GetByAnexoAsync(anexoId);
            return horarios == null ? NotFound() : Ok(horarios);
        }

        [HttpPut]
        [Authorize(Policy = ResponsibilityPolicies.AnexosEditar)]
        public async Task<IActionResult> Put(int anexoId, GuardarHorariosAnexoRequest request)
        {
            try
            {
                var ok = await _service.ReplaceByAnexoAsync(anexoId, request);
                return ok ? NoContent() : NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
