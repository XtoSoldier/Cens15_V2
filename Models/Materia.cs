namespace CENS15_V2.Models
{
    public class Materia
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int CursoId { get; set; }

        public Curso Curso { get; set; } = null!;
        public ICollection<MateriaDocente> Docentes { get; set; } = new List<MateriaDocente>();
    }
}
