using CENS15_V2.Models.DTOs.CursadasMateriasDTOs;
using CENS15_V2.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using CENS15_V2.Security;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CENS15_V2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CursadasMateriasController : ControllerBase
    {
        private readonly ICursadaMateriaService _service;

        public CursadasMateriasController(ICursadaMateriaService service)
        {
            _service = service;
        }

        [HttpPost]
        [Authorize(Policy = ResponsibilityPolicies.CursadasMateriasCrear)]
        public async Task<IActionResult> Post(CreateCursadaMateriaRequest request)
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
        [Authorize(Policy = ResponsibilityPolicies.CursadasMateriasEditar)]
        public async Task<IActionResult> Put(int id, UpdateCursadaMateriaRequest request)
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

        [HttpGet("{id:int}")]
        [Authorize(Policy = ResponsibilityPolicies.CursadasMateriasConsultar)]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpGet("/api/inscripciones/{inscripcionId:int}/cursadas-materias")]
        [Authorize(Policy = ResponsibilityPolicies.CursadasMateriasConsultar)]
        public async Task<IActionResult> GetByInscripcion(int inscripcionId)
        {
            return Ok(await _service.GetByInscripcionIdAsync(inscripcionId));
        }

        [HttpPost("para-calificar")]
        [Authorize(Policy = ResponsibilityPolicies.CalificacionesCrear)]
        public async Task<IActionResult> EnsureParaCalificar(CreateCursadaMateriaRequest request, [FromServices] AppDbContext db)
        {
            if (!ResponsibilityPolicies.TryGetUserId(User, out var userId) || ResponsibilityPolicies.IsPreceptor(User))
                return Forbid();
            var materia = await db.Materias.AsNoTracking().SingleOrDefaultAsync(m => m.Id == request.MateriaId);
            var inscripcion = await db.Inscripciones.AsNoTracking().SingleOrDefaultAsync(i => i.Id == request.InscripcionId);
            if (materia == null || inscripcion == null) return NotFound();
            if (materia.CursoId != inscripcion.CursoId) return Forbid();
            if (!ResponsibilityPolicies.IsSuperAdmin(User) &&
                (!ResponsibilityPolicies.IsDocente(User) || !await db.Users.AnyAsync(u => u.Id == userId && u.Status)
                || !await db.MateriaDocentes.AnyAsync(md => md.MateriaId == materia.Id && md.Activo && md.Docente.UserId == userId)))
                return Forbid();

            var existingId = await db.CursadasMaterias.Where(cm => cm.MateriaId == materia.Id && cm.InscripcionId == inscripcion.Id)
                .Select(cm => (int?)cm.Id).SingleOrDefaultAsync();
            if (existingId.HasValue) return Ok(await _service.GetByIdAsync(existingId.Value));
            try
            {
                return Ok(await _service.CreateAsync(request));
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.UniqueViolation
                && pg.ConstraintName == "IX_CursadasMaterias_InscripcionId_MateriaId")
            {
                // Another explicit save may have created the same enrollment/subject pair.
                var id = await db.CursadasMaterias.Where(cm => cm.MateriaId == materia.Id && cm.InscripcionId == inscripcion.Id)
                    .Select(cm => cm.Id).SingleAsync();
                return Ok(await _service.GetByIdAsync(id));
            }
            catch (InvalidOperationException ex)
            {
                var id = await db.CursadasMaterias.Where(cm => cm.MateriaId == materia.Id && cm.InscripcionId == inscripcion.Id)
                    .Select(cm => (int?)cm.Id).SingleOrDefaultAsync();
                return id.HasValue ? Ok(await _service.GetByIdAsync(id.Value)) : BadRequest(ex.Message);
            }
        }
    }
}
