namespace CENS15_V2.Models.DTOs.AlumnoDocumentosDTOs
{
    public class AlumnoDocumentoItemDto
    {
        public int Id { get; set; }
        public int AlumnoId { get; set; }
        public int TipoDocumentoAlumnoId { get; set; }
        public string TipoDocumentoAlumnoNombre { get; set; } = string.Empty;
        public bool Presentado { get; set; }
        public string? ImagenUrl { get; set; }
    }
}
