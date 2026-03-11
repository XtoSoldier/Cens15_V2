namespace CENS15_V2.Models
{
    public class AlumnoContacto
    {
        public int Id { get; set; }

        public string TelefonoAlumno { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string NombreEmergencia { get; set; } = string.Empty;

        public string TelefonoEmergencia { get; set; } = string.Empty;

        public int AlumnoId { get; set; }

        public Alumno Alumno { get; set; } = null!;
    }
}
