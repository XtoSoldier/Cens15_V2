namespace CENS15_V2.Models.DTOs.AlumnoDocumentosDTOs
{
    public class CreateAlumnoDocumentoItemRequest
    {
        public int AlumnoId { get; set; }
        public int TipoDocumentoAlumnoId { get; set; }
        public bool Presentado { get; set; }
        public string? ImagenUrl { get; set; }
    }
}
