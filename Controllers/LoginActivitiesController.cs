using CENS15_V2.Models.DTOs.LoginActivitiesDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LoginActivitiesController : ControllerBase
    {
        private readonly ILoginActivityService _service;

        public LoginActivitiesController(ILoginActivityService service)
        {
            _service = service;
        }

        [HttpGet]
        [Authorize(Policy = ResponsibilityPolicies.ActividadLoginConsultar)]
        public async Task<IActionResult> GetAll()
        {
            return Ok(await _service.GetAllAsync());
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Register(CreateLoginActivityRequest request)
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _service.RegisterAsync(request, ipAddress);
            return NoContent();
        }
    }
}
