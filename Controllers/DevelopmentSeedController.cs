using CENS15_V2.Data;
using CENS15_V2.Helper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/development/seed")]
    [Authorize]
    public class DevelopmentSeedController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly PasswordHasher _hasher;
        private readonly IWebHostEnvironment _environment;

        public DevelopmentSeedController(
            AppDbContext context,
            PasswordHasher hasher,
            IWebHostEnvironment environment)
        {
            _context = context;
            _hasher = hasher;
            _environment = environment;
        }

        [HttpPost("demo-data")]
        public async Task<IActionResult> SeedDemoData()
        {
            if (!_environment.IsDevelopment() || !IsAdministrator())
            {
                return Forbid();
            }

            await DbSeeder.SeedDemoData(_context, _hasher);
            return Ok("Datos de demostración generados.");
        }

        [HttpDelete("demo-data")]
        public async Task<IActionResult> ClearDemoData()
        {
            if (!_environment.IsDevelopment() || !IsAdministrator())
            {
                return Forbid();
            }

            await DbSeeder.ClearAcademicDemoData(_context);
            return NoContent();
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
