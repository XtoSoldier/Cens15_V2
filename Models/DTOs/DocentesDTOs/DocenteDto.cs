namespace CENS15_V2.Models.DTOs.DocentesDTOs
{
    public class DocenteDto
    {
        public int Id { get; set; }
        public string Nombres { get; set; } = string.Empty;
        public string Apellidos { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public List<DocenteMateriaDto> Materias { get; set; } = new();
    }
}
