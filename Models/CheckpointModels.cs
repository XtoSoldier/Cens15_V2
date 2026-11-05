using System.Text.Json;

namespace CENS15_V2.Models;

public sealed record CheckpointTable(string Entidad, int Cantidad);
public sealed record CheckpointMetadata(Guid Id, string Nombre, string? Descripcion, DateTimeOffset CreadoEn,
    string CreadoPor, bool Automatico, CheckpointTable[] Tablas, int CantidadArchivos, long TamanoBytes, string HuellaEsquema);
public sealed record CheckpointCreate(string Nombre, string? Descripcion);
public sealed record CheckpointExport(string Clave);
public sealed record CheckpointRestore(string Huella, string Confirmacion, bool CrearRespaldo = true, bool AceptarSinRespaldo = false);
public sealed record CheckpointCounts(string Entidad, int Actual, int Snapshot);
public sealed record CheckpointPreview(Guid CheckpointId, string Nombre, DateTimeOffset CreadoEn, bool PuedeRestaurar,
    CheckpointCounts[] Tablas, int ArchivosActuales, int ArchivosSnapshot, string[] Advertencias, string[] Errores, string Huella);
public sealed record CheckpointRestored(bool Restaurado, Guid? RespaldoPrevioId, bool RequiereReingreso, string Mensaje);

// Only scalar values named by the local EF model are accepted. No CLR type names or SQL from an archive are executed.
public sealed class ApplicationSnapshot
{
    public int Version { get; set; } = 1;
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public string CreadoPor { get; set; } = "";
    public bool Automatico { get; set; }
    public string HuellaEsquema { get; set; } = "";
    public string[] Migraciones { get; set; } = [];
    public SortedDictionary<string, List<SortedDictionary<string, JsonElement>>> Entidades { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, byte[]> Archivos { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CheckpointException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}
