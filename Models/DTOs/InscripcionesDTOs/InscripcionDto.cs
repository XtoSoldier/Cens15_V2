using CENS15_V2.Models;

namespace CENS15_V2.Models.DTOs.InscripcionesDTOs
{
    public class InscripcionDto
    {
        public int Id { get; set; }
        public int AlumnoId { get; set; }
        public string Alumno { get; set; } = string.Empty;
        public int CursoId { get; set; }
        public string Curso { get; set; } = string.Empty;
        public string CursoNombre { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;
        public int Anio { get; set; }
        public DateTime FechaInscripcion { get; set; }
        public EstadoInscripcion Estado { get; set; }
    }
}
