namespace CENS15_V2.Models
{
    public class CertificadoTemplate
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string ContenidoHtml { get; set; } = string.Empty;

        public string Formato { get; set; } = "A4";
        public decimal MargenSuperior { get; set; } = 20;
        public decimal MargenInferior { get; set; } = 20;
        public decimal MargenIzquierdo { get; set; } = 25;
        public decimal MargenDerecho { get; set; } = 25;

        public string? ImagenesJson { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
