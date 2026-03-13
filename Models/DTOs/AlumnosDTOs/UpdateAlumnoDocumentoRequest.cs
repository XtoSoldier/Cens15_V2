namespace CENS15_V2.Models.DTOs.AlumnosDTOs
{
    public class UpdateAlumnoDocumentoRequest
    {
        public int TipoDocumentoAlumnoId { get; set; }
        public bool Presentado { get; set; }
        public string? ImagenUrl { get; set; }
    }
}
