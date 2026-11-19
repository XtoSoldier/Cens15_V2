using System.ComponentModel.DataAnnotations;

namespace CENS15_V2.Models.DTOs.PreceptoresDTOs;

public class GuardarPreceptorRequest
{
    [Required]
    public string Nombres { get; set; } = string.Empty;
    [Required]
    public string Apellidos { get; set; } = string.Empty;
    public string? Email { get; set; }
    public Guid? UserId { get; set; }
    [Required]
    public List<int> CursoIds { get; set; } = new();
}
