namespace CENS15_V2.Models.DTOs.AlumnosDTOs;

public sealed class AlumnoPermanentDeletionInfoDto
{
    public int AlumnoId { get; set; }
    public bool PuedeEliminar { get; set; }
    public List<AlumnoDeletionDependencyDto> Dependencias { get; set; } = [];
    public string[] OrdenEliminacion { get; set; } = [];
    public string Mensaje { get; set; } = string.Empty;
}

public sealed class AlumnoDeletionDependencyDto
{
    public string Entidad { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}
