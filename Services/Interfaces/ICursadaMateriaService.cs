using CENS15_V2.Models.DTOs.CursadasMateriasDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface ICursadaMateriaService
    {
        Task<CursadaMateriaDto> CreateAsync(CreateCursadaMateriaRequest request);
        Task<CursadaMateriaDto?> GetByIdAsync(int id);
        Task<IEnumerable<CursadaMateriaDto>> GetByInscripcionIdAsync(int inscripcionId);
        Task<bool> UpdateAsync(int id, UpdateCursadaMateriaRequest request);
    }
}
