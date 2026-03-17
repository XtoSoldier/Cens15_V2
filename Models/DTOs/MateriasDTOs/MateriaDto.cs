namespace CENS15_V2.Models.DTOs.MateriasDTOs
{
    public class MateriaDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int CursoId { get; set; }
        public string Curso { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;
        public List<MateriaDocenteDto> Docentes { get; set; } = new();
    }
}
