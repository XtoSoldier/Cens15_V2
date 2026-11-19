using CENS15_V2.Controllers;
using CENS15_V2.Data;
using CENS15_V2.Models.DTOs.DocentesDTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Security;

// Resource authorization is based on enrollment courses, never on IDs supplied by the UI.
public sealed class AcademicAccessFilter(AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (!ResponsibilityPolicies.TryGetUserId(user, out var userId))
        {
            context.Result = new ForbidResult();
            return;
        }
        if (ResponsibilityPolicies.IsDocente(user))
        {
            if (!await db.Users.AnyAsync(u => u.Id == userId && u.Status))
            {
                context.Result = new ForbidResult();
                return;
            }
            var assignments = db.MateriaDocentes.Where(md => md.Activo && md.Docente.UserId == userId);
            var teacherCursadas = db.CursadasMaterias.Where(cm => cm.Inscripcion.CursoId == cm.Materia.CursoId
                && assignments.Any(md => md.MateriaId == cm.MateriaId));
            if (context.Controller is CalificacionesController && context.HttpContext.Request.Method == "GET")
            {
                var resource = context.ActionArguments.TryGetValue("cursadaMateriaId", out var cursadaId)
                    ? db.CursadasMaterias.Where(cm => cm.Id == (int)cursadaId!)
                    : db.CursadasMaterias.Where(cm => db.Calificaciones.Any(c => c.Id == (int)context.ActionArguments["id"]!
                        && c.CursadaMateriaId == cm.Id));
                if (await resource.AnyAsync() && !await resource.AnyAsync(cm => teacherCursadas.Any(allowed => allowed.Id == cm.Id)))
                {
                    context.Result = new ForbidResult();
                    return;
                }
            }
            var teacherExecuted = await next();
            if (context.Controller is DocentesController && teacherExecuted.Result is OkObjectResult teacherResult
                && teacherResult.Value is IEnumerable<DocenteMateriaConAlumnosDto> sheets)
            {
                var materiaIds = await assignments.Select(md => md.MateriaId).ToArrayAsync();
                teacherResult.Value = sheets.Where(m => materiaIds.Contains(m.MateriaId)).ToArray();
            }
            return;
        }
        if (!ResponsibilityPolicies.IsPreceptor(user))
        {
            await next();
            return;
        }

        var courses = db.Cursos.Where(c => c.Preceptor != null && c.Preceptor.UserId == userId);
        var allowedCursadas = db.CursadasMaterias.Where(cm => courses.Any(c => c.Id == cm.Inscripcion.CursoId));
        if (context.Controller is CalificacionesController)
        {
            if (context.HttpContext.Request.Method != "GET")
            {
                context.Result = new ForbidResult();
                return;
            }
            var allowed = context.ActionArguments.TryGetValue("cursadaMateriaId", out var cursadaId)
                ? await allowedCursadas.AnyAsync(cm => cm.Id == (int)cursadaId!)
                : await db.Calificaciones.AnyAsync(c => c.Id == (int)context.ActionArguments["id"]!
                    && allowedCursadas.Any(cm => cm.Id == c.CursadaMateriaId));
            if (!allowed)
            {
                context.Result = new ForbidResult();
                return;
            }
        }

        if (context.Controller is CertificadosController && context.ActionArguments.ContainsKey("alumnoId"))
        {
            var alumnoId = (int)context.ActionArguments["alumnoId"]!;
            var allowedIds = await db.Inscripciones.Where(i => i.AlumnoId == alumnoId
                && courses.Any(c => c.Id == i.CursoId)).Select(i => i.Id).ToArrayAsync();
            var value = context.ActionArguments["inscripcionIds"] as string;
            var selected = allowedIds;
            if (!string.IsNullOrWhiteSpace(value))
            {
                var parts = value.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Any(p => !int.TryParse(p, out var id) || !allowedIds.Contains(id)))
                {
                    context.Result = new ForbidResult();
                    return;
                }
                selected = parts.Select(int.Parse).Distinct().ToArray();
            }
            if (selected.Length == 0)
            {
                context.Result = new ForbidResult();
                return;
            }
            context.ActionArguments["inscripcionIds"] = string.Join(',', selected);
        }

        var executed = await next();
        if (context.Controller is DocentesController && executed.Result is OkObjectResult result
            && result.Value is IEnumerable<DocenteMateriaConAlumnosDto> materias)
        {
            var ids = (await allowedCursadas.Select(cm => cm.Id).ToListAsync()).ToHashSet();
            var scoped = materias.ToList();
            foreach (var materia in scoped)
                materia.Alumnos = materia.Alumnos.Where(a => ids.Contains(a.CursadaMateriaId)).ToList();
            result.Value = scoped.Where(m => m.Alumnos.Count > 0).ToArray();
        }
    }
}
