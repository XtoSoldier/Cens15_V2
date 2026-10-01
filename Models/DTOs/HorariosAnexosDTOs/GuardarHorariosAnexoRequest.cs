using CENS15_V2.Models;

namespace CENS15_V2.Models.DTOs.HorariosAnexosDTOs
{
    public class GuardarHorariosAnexoRequest
    {
        public List<HorarioAnexoRequest> Bloques { get; set; } = new();
    }

    public class HorarioAnexoRequest
    {
        public DayOfWeek DiaSemana { get; set; }
        public int Orden { get; set; }
        public TipoBloqueHorario Tipo { get; set; }
        public TimeOnly HoraInicio { get; set; }
        public TimeOnly HoraFin { get; set; }
    }
}
