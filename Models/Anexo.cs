namespace CENS15_V2.Models
{
    public class Anexo
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;

        public ICollection<Curso> Cursos { get; set; } = new List<Curso>();
    }
}
