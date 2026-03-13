using CENS15_V2.Models;

namespace CENS15_V2.Models.DTOs.AlumnosDTOs
{
    public class UpdateAlumnoRequest
    {
        public string Nombres { get; set; } = string.Empty;
        public string Apellidos { get; set; } = string.Empty;
        public string NumeroDocumento { get; set; } = string.Empty;
        public DateOnly FechaNacimiento { get; set; }
        public Genero Genero { get; set; }
        public string Domicilio { get; set; } = string.Empty;
        public UpdateAlumnoNacimientoRequest DatosNacimiento { get; set; } = new();
        public UpdateAlumnoContactoRequest Contacto { get; set; } = new();
        public ICollection<UpdateAlumnoDocumentoRequest> Documentos { get; set; } = new List<UpdateAlumnoDocumentoRequest>();
    }
}
