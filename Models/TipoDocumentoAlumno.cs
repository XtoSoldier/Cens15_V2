namespace CENS15_V2.Models
{
    public class TipoDocumentoAlumno
    {
        public int Id { get; set; }

        public string Nombre { get; set; } = string.Empty;

        public ICollection<AlumnoDocumento> Documentos { get; set; } = new List<AlumnoDocumento>();
    }
}
