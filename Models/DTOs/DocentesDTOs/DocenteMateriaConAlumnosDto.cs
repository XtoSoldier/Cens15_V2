namespace CENS15_V2.Models.DTOs.DocentesDTOs
{
    public class DocenteMateriaConAlumnosDto
    {
        public int MateriaId { get; set; }
        public string MateriaNombre { get; set; } = string.Empty;
        public int CursoId { get; set; }
        public string CursoNombre { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;
        public string? CursoLabel { get; set; }
        public int Anio { get; set; }
        public List<AlumnoCalificacionSimpleDto> Alumnos { get; set; } = new();
    }

    public class AlumnoCalificacionSimpleDto
    {
        public int AlumnoId { get; set; }
        public string AlumnoNombre { get; set; } = string.Empty;
        public int CursadaMateriaId { get; set; }
        public int? CalificacionId { get; set; }
        public decimal? C1Promedio { get; set; }
        public decimal? C2Promedio { get; set; }
        public decimal? PromedioAnual { get; set; }
        public decimal? RecuperacionDiciembre { get; set; }
        public decimal? RecuperacionMarzo { get; set; }
        public decimal? CalificacionFinal { get; set; }
        public int Estado { get; set; }
    }
}
