using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.CursosDTOs;
using CENS15_V2.Models.DTOs.PreceptoresDTOs;
using CENS15_V2.Models.DTOs.UsersDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services;

public class PreceptorConflictException(Exception inner)
    : InvalidOperationException("Conflicto de concurrencia o integridad. Actualiza los preceptores y vuelve a intentar.", inner);

public class PreceptorService(AppDbContext db, IMapper mapper) : IPreceptorService
{
    private IQueryable<Preceptor> Query() => db.Preceptores
        .Include(p => p.Cursos).ThenInclude(c => c.Anexo)
        .Include(p => p.Cursos).ThenInclude(c => c.Orientacion);

    public async Task<IEnumerable<PreceptorDto>> GetAllAsync() => mapper.Map<List<PreceptorDto>>(
        await Query().AsNoTracking().OrderBy(p => p.Apellidos).ThenBy(p => p.Nombres).ThenBy(p => p.Id).ToListAsync());

    public async Task<PreceptorDto?> GetByIdAsync(int id)
    {
        var item = await Query().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return item == null ? null : mapper.Map<PreceptorDto>(item);
    }

    public async Task<PreceptorOpcionesDto> GetOpcionesAsync()
    {
        var usuarios = await db.Users.AsNoTracking()
            .Where(u => u.Role.Name.Trim().ToLower() == "preceptor")
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ThenBy(u => u.Id)
            .Select(u => new UserDTO
            {
                Id = u.Id, FirstName = u.FirstName, LastName = u.LastName,
                Image = u.Image, Status = u.Status, RoleId = u.RoleId, Role = u.Role.Name,
                Email = u.Auth != null ? u.Auth.Email : null
            }).ToListAsync();
        var cursos = await db.Cursos.AsNoTracking().Include(c => c.Anexo).Include(c => c.Orientacion)
            .OrderBy(c => c.CursoNombre).ThenBy(c => c.Division).ThenBy(c => c.Id).ToListAsync();
        return new PreceptorOpcionesDto { Usuarios = usuarios, Cursos = mapper.Map<List<CursoDto>>(cursos) };
    }

    public Task<PreceptorDto> CreateAsync(GuardarPreceptorRequest request) => PreceptorWriteGuard.ExecuteAsync(db, async () =>
    {
        var cursos = await ValidateAsync(request);
        var item = new Preceptor
        {
            Nombres = request.Nombres.Trim(), Apellidos = request.Apellidos.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            UserId = request.UserId, Cursos = cursos
        };
        db.Preceptores.Add(item);
        await db.SaveChangesAsync();
        return mapper.Map<PreceptorDto>(item);
    });

    public Task<bool> UpdateAsync(int id, GuardarPreceptorRequest request) => PreceptorWriteGuard.ExecuteAsync(db, async () =>
    {
        var item = await Query().FirstOrDefaultAsync(p => p.Id == id);
        if (item == null) return false;
        var cursos = await ValidateAsync(request, id);

        item.Nombres = request.Nombres.Trim();
        item.Apellidos = request.Apellidos.Trim();
        item.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        item.UserId = request.UserId;
        item.Version = Guid.NewGuid();
        foreach (var curso in item.Cursos.Where(c => !request.CursoIds.Contains(c.Id)).ToList())
        {
            curso.PreceptorId = null;
            curso.Preceptor = null;
            item.Cursos.Remove(curso);
        }
        foreach (var curso in cursos.Where(c => c.PreceptorId != id))
            item.Cursos.Add(curso);
        await db.SaveChangesAsync();
        return true;
    });

    public Task<bool> DeleteAsync(int id) => PreceptorWriteGuard.ExecuteAsync(db, async () =>
    {
        var item = await db.Preceptores.Include(p => p.Cursos).FirstOrDefaultAsync(p => p.Id == id);
        if (item == null) return false;
        foreach (var curso in item.Cursos.ToList())
        {
            curso.PreceptorId = null;
            curso.Preceptor = null;
        }
        db.Preceptores.Remove(item);
        await db.SaveChangesAsync();
        return true;
    });

    private async Task<List<Curso>> ValidateAsync(GuardarPreceptorRequest request, int? id = null)
    {
        if (string.IsNullOrWhiteSpace(request.Nombres) || string.IsNullOrWhiteSpace(request.Apellidos))
            throw new InvalidOperationException("Los nombres y apellidos del preceptor son obligatorios.");
        if (request.CursoIds == null || request.CursoIds.Count != request.CursoIds.Distinct().Count())
            throw new InvalidOperationException("La lista de cursos es obligatoria y no admite ids duplicados.");
        if (!string.IsNullOrWhiteSpace(request.Email) && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email.Trim()))
            throw new InvalidOperationException("El email indicado no es valido.");
        if (request.UserId.HasValue)
        {
            var user = await db.Users.AsNoTracking().Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == request.UserId);
            if (user == null) throw new InvalidOperationException("El usuario indicado no existe.");
            if (!IsPreceptorRole(user.Role?.Name))
                throw new InvalidOperationException("El usuario asociado debe tener rol Preceptor.");
            if (await db.Preceptores.AnyAsync(p => p.Id != id && p.UserId == request.UserId))
                throw new InvalidOperationException("El usuario ya esta vinculado a otro preceptor.");
        }
        var cursos = await db.Cursos.Include(c => c.Anexo).Include(c => c.Orientacion)
            .Where(c => request.CursoIds.Contains(c.Id)).ToListAsync();
        if (cursos.Count != request.CursoIds.Count)
            throw new InvalidOperationException("Uno o mas cursos no existen.");
        if (cursos.Any(c => c.PreceptorId.HasValue && c.PreceptorId != id))
            throw new InvalidOperationException("Uno o mas cursos ya estan asignados a otro preceptor.");
        return cursos;
    }

    public static bool IsPreceptorRole(string? name) => string.Equals(name?.Trim(), "Preceptor", StringComparison.OrdinalIgnoreCase);

}
