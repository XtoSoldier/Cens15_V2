namespace CENS15_V2.Models
{
    public class CursadaMateria
    {
        public int Id { get; set; }

        public int InscripcionId { get; set; }
        public Inscripcion Inscripcion { get; set; } = null!;

        public int MateriaId { get; set; }
        public Materia Materia { get; set; } = null!;

        public string MateriaNombre { get; set; } = string.Empty;

        public Calificacion Calificacion { get; set; } = null!;
    }
}
