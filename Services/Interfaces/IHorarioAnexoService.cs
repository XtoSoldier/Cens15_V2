using CENS15_V2.Models.DTOs.HorariosAnexosDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IHorarioAnexoService
    {
        Task<IEnumerable<HorarioAnexoDto>?> GetByAnexoAsync(int anexoId);
        Task<bool> ReplaceByAnexoAsync(int anexoId, GuardarHorariosAnexoRequest request);
    }
}
