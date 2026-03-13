namespace CENS15_V2.Models
{
    public class Orientacion
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string NombreCorto { get; set; } = string.Empty;

        public ICollection<Curso> Cursos { get; set; } = new List<Curso>();
    }
}
