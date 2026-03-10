using CENS15_V2.Models.DTOs.ResponsibilitiesDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ResponsibilitiesController : ControllerBase
    {
        private readonly IResponsibilityService _service;

        public ResponsibilitiesController(IResponsibilityService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var responsibility = await _service.GetByIdAsync(id);
            return responsibility == null ? NotFound() : Ok(responsibility);
        }

        [HttpPost]
        public async Task<IActionResult> Post(CreateResponsibilityRequest request)
        {
            var created = await _service.CreateAsync(request);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(Guid id, UpdateResponsibilityRequest request)
        {
            var ok = await _service.UpdateAsync(id, request);
            return ok ? NoContent() : NotFound();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var ok = await _service.DeleteAsync(id);
            return ok ? NoContent() : NotFound();
        }
    }
}
