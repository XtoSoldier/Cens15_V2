using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AlumnosDTOs;
using CENS15_V2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using CENS15_V2.Migrations;
using System.Reflection;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class AlumnoNullableTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AlumnoService Service(AppDbContext db) => new(db,
        new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task CreationAllowsMissingDetailsAndDoesNotEnroll(string? document)
    {
        using var db = Db();
        var service = Service(db);
        var request = new CreateAlumnoRequest { Nombres = "Ana", Apellidos = "Perez", NumeroDocumento = document };
        var first = await service.CreateAsync(request);
        await service.CreateAsync(request);
        Assert.Null(first.NumeroDocumento);
        Assert.Null(first.FechaNacimiento);
        Assert.Null(first.Genero);
        Assert.Equal(2, await db.Alumnos.CountAsync());
        Assert.Empty(db.Inscripciones);
    }

    [Fact]
    public async Task UpdateCanClearDetails()
    {
        using var db = Db();
        var service = Service(db);
        var created = await service.CreateAsync(new() { Nombres = "Ana", Apellidos = "Perez", NumeroDocumento = "123",
            FechaNacimiento = new(2000, 1, 1), Genero = Genero.Mujer });
        Assert.True(await service.UpdateAsync(created.Id, new() { Nombres = "Ana", Apellidos = "Perez", NumeroDocumento = " " }));
        var updated = await service.GetByIdAsync(created.Id);
        Assert.Null(updated!.NumeroDocumento);
        Assert.Null(updated.FechaNacimiento);
        Assert.Null(updated.Genero);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PresentDocumentRemainsUniqueIncludingDeletedStudents(bool deleted)
    {
        using var db = Db();
        var service = Service(db);
        var created = await service.CreateAsync(new() { Nombres = "Ana", Apellidos = "Perez", NumeroDocumento = " 123 " });
        Assert.Equal("123", created.NumeroDocumento);
        if (deleted) await service.DeleteAsync(created.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new()
            { Nombres = "Otra", Apellidos = "Persona", NumeroDocumento = "123" }));
        var other = await service.CreateAsync(new() { Nombres = "Otra", Apellidos = "Persona" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(other.Id, new()
            { Nombres = "Otra", Apellidos = "Persona", NumeroDocumento = " 123 " }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task ProvidedInvalidGenderIsRejected(int gender)
    {
        using var db = Db();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(new()
            { Nombres = "Ana", Apellidos = "Perez", Genero = (Genero)gender }));
        Assert.Empty(db.Alumnos);
    }

    [Fact]
    public async Task ProvidedInvalidBirthDateIsRejected()
    {
        using var db = Db();
        foreach (var date in new[] { DateOnly.MinValue, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1) })
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(new()
                { Nombres = "Ana", Apellidos = "Perez", FechaNacimiento = date }));
        Assert.Empty(db.Alumnos);
    }

    [Fact]
    public void RelationalModelKeepsUniqueDocumentIndexAndNullableColumns()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        var alumno = db.Model.FindEntityType(typeof(Alumno))!;
        foreach (var name in new[] { nameof(Alumno.NumeroDocumento), nameof(Alumno.FechaNacimiento), nameof(Alumno.Genero) })
            Assert.True(alumno.FindProperty(name)!.IsNullable);
        Assert.True(alumno.GetIndexes().Single(i => i.Properties.SingleOrDefault()?.Name == nameof(Alumno.NumeroDocumento)).IsUnique);
    }

    [Theory]
    [InlineData("Up", true)]
    [InlineData("Down", false)]
    public void MigrationOnlyChangesThreeAlumnoColumnsWithoutInventedDefaults(string direction, bool nullable)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AllowMissingAlumnoPersonalData).GetMethod(direction, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AllowMissingAlumnoPersonalData(), new object[] { builder });
        Assert.Equal(3, builder.Operations.Count);
        Assert.All(builder.Operations, operation =>
        {
            var alter = Assert.IsType<AlterColumnOperation>(operation);
            Assert.Equal("Alumnos", alter.Table);
            Assert.Equal(nullable, alter.IsNullable);
            Assert.Null(alter.DefaultValue);
            Assert.Contains(alter.Name, new[] { "NumeroDocumento", "Genero", "FechaNacimiento" });
        });
    }
}
