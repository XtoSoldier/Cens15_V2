using System.Text.Json;
using System.Text.Json.Serialization;
using CENS15_V2.Models;

namespace CENS15_V2.Models.DTOs.Transferencias;

public sealed record PaqueteTransferencia
{
    public required string Formato { get; init; }
    public required int Version { get; init; }
    public required string Seccion { get; init; }
    public required DateTimeOffset ExportadoEn { get; init; }
    public required DatosTransferencia Datos { get; init; }
    public string[] Alcance { get; init; } = [];
    public string[] Exclusiones { get; init; } = [];
}

// The original eight lists remain mandatory. V1 files predate the three security lists.
public sealed record DatosTransferencia
{
    public required List<DocenteTransferencia> Docentes { get; init; }
    public required List<AnexoTransferencia> Anexos { get; init; }
    public required List<OrientacionTransferencia> Orientaciones { get; init; }
    public required List<CursoTransferencia> Cursos { get; init; }
    public required List<MateriaTransferencia> Materias { get; init; }
    public required List<BloqueTransferencia> Bloques { get; init; }
    public required List<AsignacionTransferencia> Asignaciones { get; init; }
    public required List<AlumnoTransferencia> Alumnos { get; init; }
    public List<UsuarioTransferencia> Usuarios { get; init; } = [];
    public List<RolTransferencia> Roles { get; init; } = [];
    public List<ResponsabilidadTransferencia> Responsabilidades { get; init; } = [];
}

public sealed record ResponsabilidadTransferencia(
    [property: JsonRequired] string Nombre, [property: JsonRequired] string? Descripcion);
public sealed record RolTransferencia(
    [property: JsonRequired] string Nombre, [property: JsonRequired] List<string> Responsabilidades);
public sealed record UsuarioTransferencia(
    [property: JsonRequired] string Email, [property: JsonRequired] string Nombres,
    [property: JsonRequired] string Apellidos, [property: JsonRequired] bool Activo,
    [property: JsonRequired] string Rol);

public sealed record DocenteTransferencia(
    [property: JsonRequired] string Nombres,
    [property: JsonRequired] string Apellidos,
    [property: JsonRequired] string? Email);
public sealed record AnexoTransferencia([property: JsonRequired] string Nombre);
public sealed record OrientacionTransferencia(
    [property: JsonRequired] string Nombre, [property: JsonRequired] string NombreCorto);
public sealed record CursoClave(
    [property: JsonRequired] string CursoNombre, [property: JsonRequired] string Division,
    [property: JsonRequired] string Anexo, [property: JsonRequired] string Orientacion);
public sealed record CursoTransferencia(
    [property: JsonRequired] CursoClave Clave, [property: JsonRequired] bool Semipresencial);
public sealed record DocenciaTransferencia(
    [property: JsonRequired] DocenteTransferencia Docente,
    [property: JsonRequired] string Rol, [property: JsonRequired] bool Activo);
public sealed record MateriaClave(
    [property: JsonRequired] CursoClave Curso, [property: JsonRequired] string Nombre);
public sealed record MateriaTransferencia(
    [property: JsonRequired] MateriaClave Clave, [property: JsonRequired] int CargaHoraria,
    [property: JsonRequired] List<DocenciaTransferencia> Docentes);
public sealed record BloqueClave(
    [property: JsonRequired] string Anexo, [property: JsonRequired] DayOfWeek DiaSemana,
    [property: JsonRequired] int Orden);
public sealed record BloqueTransferencia(
    [property: JsonRequired] BloqueClave Clave, [property: JsonRequired] TipoBloqueHorario Tipo,
    [property: JsonRequired] TimeOnly HoraInicio, [property: JsonRequired] TimeOnly HoraFin);
public sealed record AsignacionTransferencia(
    [property: JsonRequired] MateriaClave Materia, [property: JsonRequired] BloqueClave Bloque);
public sealed record NacimientoTransferencia(
    [property: JsonRequired] string Localidad, [property: JsonRequired] string Provincia,
    [property: JsonRequired] string Pais);
public sealed record ContactoTransferencia(
    [property: JsonRequired] string TelefonoAlumno, [property: JsonRequired] string Email,
    [property: JsonRequired] string NombreEmergencia, [property: JsonRequired] string TelefonoEmergencia);
public sealed record AlumnoTransferencia(
    [property: JsonRequired] string? NumeroDocumento, [property: JsonRequired] string Nombres,
    [property: JsonRequired] string Apellidos, [property: JsonRequired] DateOnly? FechaNacimiento,
    [property: JsonRequired] Genero? Genero, [property: JsonRequired] string Domicilio,
    [property: JsonRequired] NacimientoTransferencia? DatosNacimiento,
    [property: JsonRequired] ContactoTransferencia? Contacto);

public sealed record SolicitudTransferencia
{
    public required JsonElement Paquete { get; init; }
    public required string Modo { get; init; }
    public string? Huella { get; init; }
    public string? Confirmacion { get; init; }
}

public sealed class ResumenTransferencia
{
    public required string Entidad { get; init; }
    public int Crear { get; set; }
    public int Actualizar { get; set; }
    public int Eliminar { get; set; }
}

public sealed class ReporteTransferencia
{
    public required string Seccion { get; init; }
    public required string Modo { get; init; }
    public bool PuedeImportar => Errores.Count == 0;
    public List<ResumenTransferencia> Resumen { get; } = new();
    public List<string> Advertencias { get; } = new();
    public List<string> Errores { get; } = new();
    public string Huella { get; set; } = string.Empty;
    public bool Importado { get; set; }
    public bool RequiereReingreso { get; set; }
}
