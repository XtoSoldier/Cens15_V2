namespace CENS15_V2.Models
{
    public class Alumno
    {
        public int Id { get; set; }

        public string Nombres { get; set; } = string.Empty;
        public string Apellidos { get; set; } = string.Empty;

        public string NumeroDocumento { get; set; } = string.Empty;

        public DateOnly FechaNacimiento { get; set; }

        public Genero Genero { get; set; }

        public string Domicilio { get; set; } = string.Empty;

        public AlumnoNacimiento DatosNacimiento { get; set; } = null!;

        public AlumnoContacto Contacto { get; set; } = null!;

        public ICollection<AlumnoDocumento> Documentos { get; set; } = new List<AlumnoDocumento>();
    }
}
