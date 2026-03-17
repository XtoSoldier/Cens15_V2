namespace CENS15_V2.Models
{
    public class MateriaDocente
    {
        public int MateriaId { get; set; }
        public int DocenteId { get; set; }
        public string Rol { get; set; } = string.Empty;

        public Materia Materia { get; set; } = null!;
        public Docente Docente { get; set; } = null!;
    }
}
