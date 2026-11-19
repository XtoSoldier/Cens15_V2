using CENS15_V2.Models.DTOs.CursosDTOs;
using CENS15_V2.Models.DTOs.UsersDTOs;

namespace CENS15_V2.Models.DTOs.PreceptoresDTOs;

public class PreceptorDto
{
    public int Id { get; set; }
    public string Nombres { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
    public string? Email { get; set; }
    public Guid? UserId { get; set; }
    public List<CursoDto> Cursos { get; set; } = new();
}

public class PreceptorOpcionesDto
{
    public List<UserDTO> Usuarios { get; set; } = new();
    public List<CursoDto> Cursos { get; set; } = new();
}
