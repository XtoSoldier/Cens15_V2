using CENS15_V2.Models.DTOs.CertificadoTemplatesDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface ICertificadoTemplateService
    {
        Task<IEnumerable<CertificadoTemplateDto>> GetAllAsync();
        Task<CertificadoTemplateDto?> GetByIdAsync(int id);
        Task<CertificadoTemplateDto> CreateAsync(CreateCertificadoTemplateRequest request);
        Task<bool> UpdateAsync(int id, UpdateCertificadoTemplateRequest request);
        Task<bool> DeleteAsync(int id);
        Task<RenderedCertificadoTemplateDto?> RenderForAlumnoAsync(int templateId, int alumnoId, IReadOnlyCollection<int>? inscripcionIds = null);
    }
}
