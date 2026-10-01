namespace CENS15_V2.Models.DTOs.AlumnosDTOs
{
    public class AlumnoDeletionInfoDto
    {
        public int AlumnoId { get; set; }
        public int InscripcionesCount { get; set; }
        public int InscripcionesActivasCount { get; set; }
        public int CalificacionesCount { get; set; }
        public bool TieneDatosAcademicos => InscripcionesCount > 0 || CalificacionesCount > 0;
        public string Mensaje { get; set; } = string.Empty;
    }
}
