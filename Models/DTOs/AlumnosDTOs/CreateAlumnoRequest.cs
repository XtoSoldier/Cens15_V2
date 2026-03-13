using CENS15_V2.Models;

namespace CENS15_V2.Models.DTOs.AlumnosDTOs
{
    public class CreateAlumnoRequest
    {
        public string Nombres { get; set; } = string.Empty;
        public string Apellidos { get; set; } = string.Empty;
        public string NumeroDocumento { get; set; } = string.Empty;
        public DateOnly FechaNacimiento { get; set; }
        public Genero Genero { get; set; }
        public string Domicilio { get; set; } = string.Empty;
        public CreateAlumnoNacimientoRequest DatosNacimiento { get; set; } = new();
        public CreateAlumnoContactoRequest Contacto { get; set; } = new();
        public ICollection<CreateAlumnoDocumentoRequest> Documentos { get; set; } = new List<CreateAlumnoDocumentoRequest>();
    }
}
