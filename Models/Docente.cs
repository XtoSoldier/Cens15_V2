using CENS15_V2.Entities;

namespace CENS15_V2.Models
{
    public class Docente
    {
        public int Id { get; set; }

        public string Nombres { get; set; } = string.Empty;
        public string Apellidos { get; set; } = string.Empty;
        public string? Email { get; set; }

        public Guid? UserId { get; set; }
        public User? User { get; set; }

        public ICollection<MateriaDocente> Materias { get; set; } = new List<MateriaDocente>();
    }
}
