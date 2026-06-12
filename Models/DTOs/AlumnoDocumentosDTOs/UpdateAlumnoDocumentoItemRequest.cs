namespace CENS15_V2.Models.DTOs.AlumnoDocumentosDTOs
{
    public class UpdateAlumnoDocumentoItemRequest
    {
        public int TipoDocumentoAlumnoId { get; set; }
        public bool Presentado { get; set; }
        public string? ImagenUrl { get; set; }
        public List<string> ImagenesUrl { get; set; } = new();
        public List<IFormFile> Imagenes { get; set; } = new();
    }
}
