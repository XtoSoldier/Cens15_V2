using CENS15_V2.Models.DTOs.PreceptoresDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models.DTOs.CursosDTOs;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PreceptoresController(IPreceptorService service) : ControllerBase
{
    [HttpGet("mis-cursos")]
    [Authorize(Policy = ResponsibilityPolicies.CalificacionesConsultar)]
    public async Task<IActionResult> GetMisCursos([FromServices] AppDbContext db, [FromServices] IMapper mapper)
    {
        if (!ResponsibilityPolicies.TryGetUserId(User, out var userId)) return Forbid();
        var cursos = await db.Cursos.AsNoTracking()
            .Include(c => c.Orientacion).Include(c => c.Anexo).Include(c => c.Preceptor)
            .Where(c => c.Preceptor != null && c.Preceptor.UserId == userId)
            .OrderBy(c => c.Id).ToListAsync();
        return Ok(mapper.Map<CursoDto[]>(cursos));
    }
    [HttpGet]
    [Authorize(Policy = ResponsibilityPolicies.PreceptoresConsultar)]
    public async Task<IActionResult> Get() => Ok(await service.GetAllAsync());

    [HttpGet("{id:int}")]
    [Authorize(Policy = ResponsibilityPolicies.PreceptoresConsultar)]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await service.GetByIdAsync(id);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpGet("opciones")]
    [Authorize(Policy = ResponsibilityPolicies.PreceptoresConsultar)]
    public async Task<IActionResult> GetOpciones() => Ok(await service.GetOpcionesAsync());

    [HttpPost]
    [Authorize(Policy = ResponsibilityPolicies.PreceptoresCrear)]
    public async Task<IActionResult> Post(GuardarPreceptorRequest request)
    {
        try
        {
            var item = await service.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
        }
        catch (PreceptorConflictException ex) { return Conflict(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = ResponsibilityPolicies.PreceptoresEditar)]
    public async Task<IActionResult> Put(int id, GuardarPreceptorRequest request)
    {
        try { return await service.UpdateAsync(id, request) ? NoContent() : NotFound(); }
        catch (PreceptorConflictException ex) { return Conflict(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = ResponsibilityPolicies.PreceptoresEliminar)]
    public async Task<IActionResult> Delete(int id)
    {
        try { return await service.DeleteAsync(id) ? NoContent() : NotFound(); }
        catch (PreceptorConflictException ex) { return Conflict(ex.Message); }
    }
}
