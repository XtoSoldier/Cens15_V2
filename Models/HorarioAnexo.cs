namespace CENS15_V2.Models
{
    public enum TipoBloqueHorario
    {
        Clase,
        Receso
    }

    public class HorarioAnexo
    {
        public int Id { get; set; }
        public int AnexoId { get; set; }
        public Anexo Anexo { get; set; } = null!;
        public DayOfWeek DiaSemana { get; set; }
        public int Orden { get; set; }
        public TipoBloqueHorario Tipo { get; set; }
        public TimeOnly HoraInicio { get; set; }
        public TimeOnly HoraFin { get; set; }
    }
}
