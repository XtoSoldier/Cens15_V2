using System.Security.Claims;
using AutoMapper;
using CENS15_V2.Controllers;
using CENS15_V2.Data;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class AlumnoPermanentDeletionTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static IMapper Mapper() => new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyStudentCanBeDeletedAndMissingReturnsFalse(bool deleted)
    {
        using var db = Db();
        var alumno = new Alumno { Nombres = "Test", Eliminado = deleted };
        db.Add(alumno);
        await db.SaveChangesAsync();
        var service = new AlumnoService(db, Mapper());
        var info = await service.GetPermanentDeletionInfoAsync(alumno.Id);
        Assert.True(info!.PuedeEliminar);
        Assert.Equal(6, info.Dependencias.Count);
        Assert.All(info.Dependencias, d => Assert.Equal(0, d.Cantidad));
        Assert.Equal("Alumnos", info.OrdenEliminacion.Last());
        Assert.True(await service.DeletePermanentlyAsync(alumno.Id));
        Assert.Empty(db.Alumnos.IgnoreQueryFilters());
        Assert.False(await service.DeletePermanentlyAsync(alumno.Id));
        Assert.Null(await service.GetPermanentDeletionInfoAsync(alumno.Id));
    }

    [Theory]
    [InlineData(false, "AlumnoDocumentos")]
    [InlineData(true, "AlumnoDocumentos")]
    [InlineData(false, "AlumnoContactos")]
    [InlineData(true, "AlumnoContactos")]
    [InlineData(false, "AlumnoNacimientos")]
    [InlineData(true, "AlumnoNacimientos")]
    [InlineData(false, "Inscripciones")]
    [InlineData(true, "Inscripciones")]
    public async Task EachDirectChildBlocksWithoutMutatingTrackedOrPersistedState(bool deleted, string child)
    {
        using var db = Db();
        var alumno = new Alumno { Nombres = "Test", Eliminado = deleted, FechaEliminacion = deleted ? DateTime.UtcNow : null };
        db.Add(alumno);
        switch (child)
        {
            case "AlumnoDocumentos": db.Add(new AlumnoDocumento { Alumno = alumno }); break;
            case "AlumnoContactos": db.Add(new AlumnoContacto { Alumno = alumno }); break;
            case "AlumnoNacimientos": db.Add(new AlumnoNacimiento { Alumno = alumno }); break;
            case "Inscripciones": db.Add(new Inscripcion { Alumno = alumno }); break;
        }
        await db.SaveChangesAsync();
        var states = db.ChangeTracker.Entries().Select(e => (e.Entity, e.State)).ToArray();
        var timestamp = alumno.FechaEliminacion;
        var service = new AlumnoService(db, Mapper());
        var info = await service.GetPermanentDeletionInfoAsync(alumno.Id);
        Assert.False(info!.PuedeEliminar);
        Assert.Equal(1, info.Dependencias.Single(d => d.Entidad == child).Cantidad);
        await Assert.ThrowsAsync<DeletionConflictException>(() => service.DeletePermanentlyAsync(alumno.Id));
        Assert.Equal(states, db.ChangeTracker.Entries().Select(e => (e.Entity, e.State)).ToArray());
        Assert.Equal(deleted, alumno.Eliminado);
        Assert.Equal(timestamp, alumno.FechaEliminacion);
        await db.SaveChangesAsync();
        var after = await service.GetPermanentDeletionInfoAsync(alumno.Id);
        Assert.Equal(1, after!.Dependencias.Single(d => d.Entidad == child).Cantidad);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AcademicDescendantsAreVisibleAndInscripcionCannotCascade(bool deleted, bool withGrade)
    {
        using var db = Db();
        var alumno = new Alumno { Eliminado = deleted };
        var inscripcion = new Inscripcion { Alumno = alumno };
        var cursada = new CursadaMateria { Inscripcion = inscripcion };
        db.Add(cursada);
        if (withGrade) db.Add(new Calificacion { CursadaMateria = cursada });
        await db.SaveChangesAsync();
        var service = new AlumnoService(db, Mapper());
        var info = await service.GetPermanentDeletionInfoAsync(alumno.Id);
        Assert.Equal(new[] { withGrade ? 1 : 0, 1, 1, 0, 0, 0 }, info!.Dependencias.Select(d => d.Cantidad));
        await Assert.ThrowsAsync<DeletionConflictException>(() => new InscripcionService(db, Mapper()).DeleteAsync(inscripcion.Id));
        await Assert.ThrowsAsync<DeletionConflictException>(() => service.DeletePermanentlyAsync(alumno.Id));
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        Assert.Equal(withGrade ? 1 : 0, await db.Calificaciones.IgnoreQueryFilters().CountAsync());
        Assert.Single(db.CursadasMaterias.IgnoreQueryFilters());
    }

    [Fact]
    public async Task EmptyInscripcionCanBeRemovedWithoutRemovingStudent()
    {
        using var db = Db();
        var inscripcion = new Inscripcion { Alumno = new Alumno { Eliminado = true } };
        db.Add(inscripcion);
        await db.SaveChangesAsync();
        Assert.True(await new InscripcionService(db, Mapper()).DeleteAsync(inscripcion.Id));
        Assert.Single(db.Alumnos.IgnoreQueryFilters());
        Assert.Empty(db.Inscripciones.IgnoreQueryFilters());
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData(" sUpEr AdMiN ", true)]
    [InlineData("Docente", false)]
    [InlineData("", false)]
    public async Task BothEndpointsCheckRolesOnServer(string role, bool allowed)
    {
        using var db = Db();
        var controller = new AlumnosController(new AlumnoService(db, Mapper()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test")) } }
        };
        var info = await controller.GetPermanentDeletionInfo(999);
        var deletion = await controller.DeletePermanently(999);
        if (allowed) { Assert.IsType<NotFoundResult>(info); Assert.IsType<NotFoundResult>(deletion); }
        else { Assert.IsType<ForbidResult>(info); Assert.IsType<ForbidResult>(deletion); }
        foreach (var name in new[] { nameof(controller.GetPermanentDeletionInfo), nameof(controller.DeletePermanently) })
            Assert.NotEmpty(typeof(AlumnosController).GetMethod(name)!.GetCustomAttributes(typeof(AuthorizeAttribute), true));
    }

    [Fact]
    public async Task ControllersReturnControlledConflictsAndLegacySoftDeleteRemainsRestorable()
    {
        using var db = Db();
        var inscripcion = new Inscripcion { Alumno = new Alumno() };
        db.Add(new CursadaMateria { Inscripcion = inscripcion });
        await db.SaveChangesAsync();
        var service = new AlumnoService(db, Mapper());
        var controller = new AlumnosController(service) { ControllerContext = new ControllerContext
            { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Test")) } } };
        Assert.IsType<ConflictObjectResult>(await controller.DeletePermanently(inscripcion.AlumnoId));
        Assert.IsType<ConflictObjectResult>(await new InscripcionesController(new InscripcionService(db, Mapper())).Delete(inscripcion.Id));
        Assert.NotNull(await service.GetDeletionInfoAsync(inscripcion.AlumnoId));
        Assert.True(await service.DeleteAsync(inscripcion.AlumnoId));
        Assert.Empty(db.Alumnos);
        Assert.Single(db.Inscripciones.IgnoreQueryFilters());
        Assert.Null(await service.GetDeletionInfoAsync(inscripcion.AlumnoId));
        Assert.True(await service.RestoreAsync(inscripcion.AlumnoId));
        Assert.Single(db.Alumnos);
    }
}
