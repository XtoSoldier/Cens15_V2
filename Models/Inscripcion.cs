namespace CENS15_V2.Models
{
    public class Inscripcion
    {
        public int Id { get; set; }

        public int AlumnoId { get; set; }
        public Alumno Alumno { get; set; } = null!;

        public int CursoId { get; set; }
        public Curso Curso { get; set; } = null!;

        public int Anio { get; set; }

        public DateTime FechaInscripcion { get; set; }

        public EstadoInscripcion Estado { get; set; }

        public string CursoNombre { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;

        public ICollection<CursadaMateria> CursadasMaterias { get; set; } = new List<CursadaMateria>();
    }
}
