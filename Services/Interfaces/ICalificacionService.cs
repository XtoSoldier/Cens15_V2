using CENS15_V2.Models.DTOs.CalificacionesDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface ICalificacionService
    {
        Task<CalificacionDto> CreateAsync(CreateCalificacionRequest request);
        Task<CalificacionDto?> GetByIdAsync(int id);
        Task<CalificacionDto?> GetByCursadaMateriaIdAsync(int cursadaMateriaId);
        Task<bool> UpdateAsync(int id, UpdateCalificacionRequest request);
    }
}
