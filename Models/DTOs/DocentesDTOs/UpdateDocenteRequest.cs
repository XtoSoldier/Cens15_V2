namespace CENS15_V2.Models.DTOs.DocentesDTOs
{
    public class UpdateDocenteRequest
    {
        public string Nombres { get; set; } = string.Empty;
        public string Apellidos { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
    }
}
