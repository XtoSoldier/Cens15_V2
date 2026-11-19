using CENS15_V2.Data;
using CENS15_V2.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CENS15_V2.Services;

internal static class PermanentDeletionGuard
{
    // Fail closed if the EF model acquires a new referencing entity/relationship.
    internal static bool HasKnownReferences(AppDbContext db) => db.Model.GetEntityTypes()
        .SelectMany(e => e.GetForeignKeys())
        .Where(f => new[] { typeof(Alumno), typeof(Inscripcion), typeof(CursadaMateria) }.Contains(f.PrincipalEntityType.ClrType))
        .All(f => f.Properties.Count == 1 &&
            ((f.PrincipalEntityType.ClrType == typeof(Alumno) && f.Properties[0].Name == "AlumnoId" &&
              new[] { typeof(AlumnoNacimiento), typeof(AlumnoContacto), typeof(AlumnoDocumento), typeof(Inscripcion) }.Contains(f.DeclaringEntityType.ClrType)) ||
             (f.PrincipalEntityType.ClrType == typeof(Inscripcion) && f.DeclaringEntityType.ClrType == typeof(CursadaMateria) && f.Properties[0].Name == "InscripcionId") ||
             (f.PrincipalEntityType.ClrType == typeof(CursadaMateria) && f.DeclaringEntityType.ClrType == typeof(Calificacion) && f.Properties[0].Name == "CursadaMateriaId")));

    internal static bool IsConflict(Exception ex) => ex is DbUpdateConcurrencyException ||
        ex is PostgresException { SqlState: "23503" or "40001" or "40P01" or "55P03" } ||
        ex.InnerException != null && IsConflict(ex.InnerException);
}
