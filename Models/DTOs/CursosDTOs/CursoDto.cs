using System.Text.Json.Serialization;

namespace CENS15_V2.Models.DTOs.CursosDTOs
{
    public class CursoDto
    {
        public int Id { get; set; }
        public string Curso { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;
        [JsonPropertyName("id_orientacion")]
        public int IdOrientacion { get; set; }
        [JsonPropertyName("id_anexo")]
        public int IdAnexo { get; set; }
        public string OrientacionNombreCorto { get; set; } = string.Empty;
        public string AnexoNombre { get; set; } = string.Empty;
        public bool Semipresencial { get; set; }
    }
}
