namespace CENS15_V2.Models
{
    public class AlumnoDocumento
    {
        public int Id { get; set; }

        public int AlumnoId { get; set; }

        public int TipoDocumentoAlumnoId { get; set; }

        public bool Presentado { get; set; }

        public string? ImagenUrl { get; set; }

        public Alumno Alumno { get; set; } = null!;

        public TipoDocumentoAlumno TipoDocumentoAlumno { get; set; } = null!;
    }
}
