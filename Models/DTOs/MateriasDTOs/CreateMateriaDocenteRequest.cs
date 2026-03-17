namespace CENS15_V2.Models.DTOs.MateriasDTOs
{
    public class CreateMateriaDocenteRequest
    {
        public int DocenteId { get; set; }
        public string Rol { get; set; } = string.Empty;
    }
}
