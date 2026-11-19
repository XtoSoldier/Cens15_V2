using CENS15_V2.Models.DTOs.PreceptoresDTOs;

namespace CENS15_V2.Services.Interfaces;

public interface IPreceptorService
{
    Task<IEnumerable<PreceptorDto>> GetAllAsync();
    Task<PreceptorDto?> GetByIdAsync(int id);
    Task<PreceptorOpcionesDto> GetOpcionesAsync();
    Task<PreceptorDto> CreateAsync(GuardarPreceptorRequest request);
    Task<bool> UpdateAsync(int id, GuardarPreceptorRequest request);
    Task<bool> DeleteAsync(int id);
}
