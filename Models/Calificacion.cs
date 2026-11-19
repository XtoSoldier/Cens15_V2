namespace CENS15_V2.Models
{
    public class Calificacion
    {
        public int Id { get; set; }

        public int CursadaMateriaId { get; set; }
        public CursadaMateria CursadaMateria { get; set; } = null!;

        public string MateriaNombre { get; set; } = string.Empty;

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
        public string? DisposicionEquivalencia { get; set; }
        public bool DesdePase { get; set; }
        public CondicionNota C1Condicion { get; set; } = CondicionNota.Numerica;
        public CondicionNota C2Condicion { get; set; } = CondicionNota.Numerica;
        public CondicionNota DiciembreCondicion { get; set; } = CondicionNota.Numerica;
        public CondicionNota MarzoCondicion { get; set; } = CondicionNota.Numerica;
        public EstadoMateria? RecuperacionExcepcional { get; set; }
    }
}
