namespace CENS15_V2.Models
{
    public class Curso
    {
        public int Id { get; set; }
        public string CursoNombre { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;
        public int OrientacionId { get; set; }
        public int AnexoId { get; set; }
        public bool Semipresencial { get; set; }

        public Orientacion Orientacion { get; set; } = null!;
        public Anexo Anexo { get; set; } = null!;
        public ICollection<Materia> Materias { get; set; } = new List<Materia>();
    }
}
