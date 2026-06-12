namespace CENS15_V2.Models.DTOs.CertificadoTemplatesDTOs
{
    public class RenderedCertificadoTemplateDto
    {
        public int TemplateId { get; set; }
        public int AlumnoId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Html { get; set; } = string.Empty;
        public string Formato { get; set; } = "A4";
        public decimal MargenSuperior { get; set; }
        public decimal MargenInferior { get; set; }
        public decimal MargenIzquierdo { get; set; }
        public decimal MargenDerecho { get; set; }
        public string? ImagenesJson { get; set; }
    }
}
