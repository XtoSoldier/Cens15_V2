using CENS15_V2.Entities;

namespace CENS15_V2.Models;

public class Preceptor
{
    public int Id { get; set; }
    public string Nombres { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
    public string? Email { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public ICollection<Curso> Cursos { get; set; } = new List<Curso>();
    public Guid Version { get; set; } = Guid.NewGuid();
}
