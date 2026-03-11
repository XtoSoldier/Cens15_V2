namespace CENS15_V2.Models
{
    public class AlumnoNacimiento
    {
        public int Id { get; set; }

        public string Localidad { get; set; } = string.Empty;

        public string Provincia { get; set; } = string.Empty;

        public string Pais { get; set; } = string.Empty;

        public int AlumnoId { get; set; }

        public Alumno Alumno { get; set; } = null!;
    }
}
