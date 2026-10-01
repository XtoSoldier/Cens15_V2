using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.CursosDTOs;
using CENS15_V2.Models.DTOs.HorariosAnexosDTOs;
using CENS15_V2.Models.DTOs.MateriasDTOs;
using CENS15_V2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CENS15_V2.Tests;

public class MateriaScheduleTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IMapper _mapper;
    private readonly MateriaService _service;

    public MateriaScheduleTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _mapper = new MapperConfiguration(config => config.AddProfile<AutoMapperProfile>()).CreateMapper();
        _service = new MateriaService(_db, _mapper);
        _db.Anexos.AddRange(new Anexo { Id = 1, Nombre = "Centro" }, new Anexo { Id = 2, Nombre = "Norte" });
        _db.Orientaciones.Add(new Orientacion { Id = 1, Nombre = "Sociales" });
        _db.Cursos.AddRange(
            new Curso { Id = 1, CursoNombre = "1°", Division = "A", AnexoId = 1, OrientacionId = 1 },
            new Curso { Id = 2, CursoNombre = "2°", Division = "B", AnexoId = 1, OrientacionId = 1 });
        _db.HorariosAnexo.AddRange(
            Block(1, 1, DayOfWeek.Monday, 1, TipoBloqueHorario.Clase, "19:10", "19:50"),
            Block(2, 1, DayOfWeek.Monday, 2, TipoBloqueHorario.Receso, "19:50", "20:00"),
            Block(3, 1, DayOfWeek.Monday, 3, TipoBloqueHorario.Clase, "20:00", "20:40"),
            Block(4, 1, DayOfWeek.Tuesday, 1, TipoBloqueHorario.Clase, "19:10", "19:50"),
            Block(5, 2, DayOfWeek.Monday, 1, TipoBloqueHorario.Clase, "19:10", "19:50"));
        _db.SaveChanges();
    }

    [Fact]
    public async Task ExactLoadReturnsSelectedDaysAndTimes()
    {
        var materia = await _service.CreateAsync(Request(2, 1, 4));
        Assert.Equal(2, materia.CargaHoraria);
        Assert.Equal(new[] { 1, 4 }, materia.Horarios.Select(h => h.Id));
        Assert.Equal(DayOfWeek.Tuesday, materia.Horarios[1].DiaSemana);
        Assert.Equal("A", materia.Division);
    }

    [Theory]
    [InlineData(2, new[] { 1 })]
    [InlineData(1, new[] { 1, 3 })]
    [InlineData(2, new[] { 1, 1 })]
    [InlineData(1, new[] { 2 })]
    [InlineData(1, new[] { 5 })]
    [InlineData(1, new[] { 99 })]
    [InlineData(0, new[] { 1 })]
    [InlineData(-1, new int[] { })]
    public async Task InvalidAssignmentsAreRejected(int carga, int[] ids)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(Request(carga, ids)));
        Assert.Empty(_db.Materias);
    }

    [Fact]
    public async Task PositiveLoadCanRemainPending()
    {
        var materia = await _service.CreateAsync(Request(3));
        Assert.Equal(3, materia.CargaHoraria);
        Assert.Empty(materia.Horarios);
    }

    [Fact]
    public async Task OccupancyIsPerCourse()
    {
        await _service.CreateAsync(Request(1, 1));
        var sameCourse = Request(1, 1);
        sameCourse.Nombre = "Historia";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(sameCourse));
        sameCourse.CursoId = 2;
        var otherCourse = await _service.CreateAsync(sameCourse);
        Assert.Single(otherCourse.Horarios);
        Assert.Equal(2, _db.HorariosMateria.Count());
    }

    [Fact]
    public async Task EditingOwnSchedulePreservesAssignmentsAndOmittedFields()
    {
        var materia = await _service.CreateAsync(Request(2, 1, 4));
        var assignmentIds = _db.HorariosMateria.Select(h => h.Id).ToArray();
        Assert.True(await _service.UpdateAsync(materia.Id, new UpdateMateriaRequest { Nombre = "Matematica II", CursoId = 1 }));
        var updated = await _service.GetByIdAsync(materia.Id);
        Assert.Equal(2, updated!.CargaHoraria);
        Assert.Equal(new[] { 1, 4 }, updated.Horarios.Select(h => h.Id));
        Assert.Equal(assignmentIds, _db.HorariosMateria.Select(h => h.Id).ToArray());
    }

    [Fact]
    public async Task ChangingLoadRequiresMatchingSelectedHours()
    {
        var materia = await _service.CreateAsync(Request(2, 1, 4));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(materia.Id,
            new UpdateMateriaRequest { Nombre = materia.Nombre, CursoId = 1, CargaHoraria = 3 }));
        Assert.Equal(2, (await _service.GetByIdAsync(materia.Id))!.CargaHoraria);
    }

    [Fact]
    public async Task ClearingScheduleFreesHoursForAnotherSubject()
    {
        var materia = await _service.CreateAsync(Request(1, 1));
        await _service.UpdateAsync(materia.Id, new UpdateMateriaRequest
        {
            Nombre = materia.Nombre, CursoId = 1, CargaHoraria = 2, HorarioIds = new()
        });
        var next = Request(1, 1);
        next.Nombre = "Historia";
        Assert.Single((await _service.CreateAsync(next)).Horarios);
        Assert.Empty((await _service.GetByIdAsync(materia.Id))!.Horarios);
    }

    [Fact]
    public async Task SavingUnchangedAnnexPreservesBlockIdentitiesAndSubjectSchedule()
    {
        var materia = await _service.CreateAsync(Request(1, 1));
        var request = GridRequest();
        request.Bloques.Single(b => b.DiaSemana == DayOfWeek.Tuesday).HoraFin = new TimeOnly(20, 0);
        await new HorarioAnexoService(_db).ReplaceByAnexoAsync(1, request);
        Assert.Equal(new[] { 1, 2, 3, 4 }, _db.HorariosAnexo.Where(h => h.AnexoId == 1).OrderBy(h => h.Id).Select(h => h.Id));
        Assert.Equal(1, (await _service.GetByIdAsync(materia.Id))!.Horarios.Single().Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AssignedBlockCannotBeChangedOrDeleted(bool delete)
    {
        await _service.CreateAsync(Request(1, 1));
        var request = GridRequest();
        if (delete) request.Bloques.RemoveAt(0);
        else request.Bloques[0].HoraInicio = new TimeOnly(19, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new HorarioAnexoService(_db).ReplaceByAnexoAsync(1, request));
        Assert.Equal(new TimeOnly(19, 10), (await _db.HorariosAnexo.FindAsync(1))!.HoraInicio);
    }

    [Fact]
    public async Task CourseCannotMoveToAnotherAnnexWhileAssigned()
    {
        await _service.CreateAsync(Request(1, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CursoService(_db, _mapper).UpdateAsync(1,
            new UpdateCursoRequest { Curso = "1°", Division = "A", IdOrientacion = 1, IdAnexo = 2 }));
        Assert.Equal(1, (await _db.Cursos.FindAsync(1))!.AnexoId);
    }

    [Fact]
    public void ModelEnforcesUniqueCoursePeriodAndRestrictsDeletingAssignedBlocks()
    {
        var entity = _db.Model.FindEntityType(typeof(HorarioMateria))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual(new[] { "CursoId", "HorarioAnexoId" }));
        Assert.Contains(entity.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(HorarioAnexo)
            && key.DeleteBehavior == DeleteBehavior.Restrict);
    }

    private GuardarHorariosAnexoRequest GridRequest() => new()
    {
        Bloques = _db.HorariosAnexo.Where(h => h.AnexoId == 1).OrderBy(h => h.Id).Select(h => new HorarioAnexoRequest
        {
            DiaSemana = h.DiaSemana, Orden = h.Orden, Tipo = h.Tipo, HoraInicio = h.HoraInicio, HoraFin = h.HoraFin
        }).ToList()
    };

    private static CreateMateriaRequest Request(int carga, params int[] ids) => new()
    {
        Nombre = "Matematica", CursoId = 1, CargaHoraria = carga, HorarioIds = ids.ToList()
    };

    private static HorarioAnexo Block(int id, int anexo, DayOfWeek day, int order, TipoBloqueHorario type, string start, string end) => new()
    {
        Id = id, AnexoId = anexo, DiaSemana = day, Orden = order, Tipo = type,
        HoraInicio = TimeOnly.Parse(start), HoraFin = TimeOnly.Parse(end)
    };

    public void Dispose() => _db.Dispose();
}
