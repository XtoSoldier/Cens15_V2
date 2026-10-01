namespace CENS15_V2.Models.DTOs.MateriasDTOs
{
    public class CreateMateriaRequest
    {
        public string Nombre { get; set; } = string.Empty;
        public int CursoId { get; set; }
        public int CargaHoraria { get; set; }
        public List<int> HorarioIds { get; set; } = new();
        public List<CreateMateriaDocenteRequest> Docentes { get; set; } = new();
    }
}
