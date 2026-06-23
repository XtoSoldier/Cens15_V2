using CENS15_V2.Models.DTOs.AuthDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _service;
        public AuthController(IAuthService service)
        {
            _service = service;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            var result = await _service.Login(request);
            if (result == null) return Unauthorized("Invalid Credentials");
            return Ok(result);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest request)
        {
            await _service.Register(request);
            return Ok("Usuario creado exitosamente");
        }

        [HttpPost("request-initial-access")]
        public async Task<IActionResult> RequestInitialAccess(InitialAccessRequest request)
        {
            try
            {
                await _service.RequestInitialAccessAsync(request);
                return Ok(new { message = "Si el correo está registrado, se envió un email con la clave de acceso inicial." });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(502, new { message = ex.Message });
            }
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
        {
            var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (!Guid.TryParse(userIdValue, out var userId))
            {
                return Unauthorized();
            }

            try
            {
                var ok = await _service.ChangePasswordAsync(userId, request);
                return ok ? NoContent() : BadRequest(new { message = "La contraseña actual no es correcta." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("request-password-reset")]
        public async Task<IActionResult> RequestPasswordReset(PasswordResetRequest request)
        {
            try
            {
                await _service.RequestPasswordResetAsync(request);
                return Ok(new { message = "Si el correo está registrado, se envió un código de recuperación." });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(502, new { message = ex.Message });
            }
        }

        [HttpPost("validate-password-reset-code")]
        public async Task<IActionResult> ValidatePasswordResetCode(ValidatePasswordResetCodeRequest request)
        {
            try
            {
                await _service.ValidatePasswordResetCodeAsync(request);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
        {
            try
            {
                await _service.ResetPasswordAsync(request);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
