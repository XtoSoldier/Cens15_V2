namespace CENS15_V2.Models.DTOs.AlumnosDTOs
{
    public class CreateAlumnoNacimientoRequest
    {
        public string Localidad { get; set; } = string.Empty;
        public string Provincia { get; set; } = string.Empty;
        public string Pais { get; set; } = string.Empty;
    }
}
