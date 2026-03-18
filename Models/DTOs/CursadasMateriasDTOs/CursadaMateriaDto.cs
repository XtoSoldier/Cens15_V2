namespace CENS15_V2.Models.DTOs.CursadasMateriasDTOs
{
    public class CursadaMateriaDto
    {
        public int Id { get; set; }
        public int InscripcionId { get; set; }
        public int MateriaId { get; set; }
        public string MateriaNombre { get; set; } = string.Empty;
    }
}
