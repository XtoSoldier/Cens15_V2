namespace CENS15_V2.Models
{
    public class Calificacion
    {
        public int Id { get; set; }

        public int CursadaMateriaId { get; set; }
        public CursadaMateria CursadaMateria { get; set; } = null!;

        // Primer cuatrimestre
        public decimal? C1Nota1 { get; set; }
        public decimal? C1Nota2 { get; set; }
        public decimal? C1Nota3 { get; set; }
        public decimal? C1Promedio { get; set; }

        // Segundo cuatrimestre
        public decimal? C2Nota1 { get; set; }
        public decimal? C2Nota2 { get; set; }
        public decimal? C2Nota3 { get; set; }
        public decimal? C2Promedio { get; set; }

        // Promedio anual
        public decimal? PromedioAnual { get; set; }

        // Recuperaciones
        public decimal? RecuperacionDiciembre { get; set; }
        public decimal? RecuperacionMarzo { get; set; }

        public decimal? CalificacionFinal { get; set; }

        public EstadoMateria Estado { get; set; }
    }
}
