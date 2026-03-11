namespace CENS15_V2.Models.DTOs.AlumnosDTOs
{
    public class AlumnoDocumentoDto
    {
        public int Id { get; set; }
        public int TipoDocumentoAlumnoId { get; set; }
        public string TipoDocumentoAlumnoNombre { get; set; } = string.Empty;
        public bool Presentado { get; set; }
    }
}
