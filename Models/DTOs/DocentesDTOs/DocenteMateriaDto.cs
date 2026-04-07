namespace CENS15_V2.Models.DTOs.DocentesDTOs
{
    public class DocenteMateriaDto
    {
        public int MateriaId { get; set; }
        public string Materia { get; set; } = string.Empty;
        public int CursoId { get; set; }
        public string Curso { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;
        public string Rol { get; set; } = string.Empty;
    }
}
