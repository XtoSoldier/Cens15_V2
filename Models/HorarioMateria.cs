namespace CENS15_V2.Models
{
    public class HorarioMateria
    {
        public int Id { get; set; }
        public int MateriaId { get; set; }
        public Materia Materia { get; set; } = null!;
        public int CursoId { get; set; }
        public Curso Curso { get; set; } = null!;
        public int HorarioAnexoId { get; set; }
        public HorarioAnexo HorarioAnexo { get; set; } = null!;
    }
}
