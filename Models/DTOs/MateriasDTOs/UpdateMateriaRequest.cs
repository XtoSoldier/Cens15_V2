namespace CENS15_V2.Models.DTOs.MateriasDTOs
{
    public class UpdateMateriaRequest
    {
        public string Nombre { get; set; } = string.Empty;
        public int CursoId { get; set; }
        public int DocenteId { get; set; }
    }
}
