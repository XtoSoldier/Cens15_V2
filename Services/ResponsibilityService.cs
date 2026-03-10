using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.ResponsibilitiesDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class ResponsibilityService : IResponsibilityService
    {
        private readonly AppDbContext _context;

        public ResponsibilityService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<ResponsibilityDto>> GetAllAsync()
        {
            return await _context.Responsibilities
                .AsNoTracking()
                .Select(r => new ResponsibilityDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description
                })
                .ToListAsync();
        }

        public async Task<ResponsibilityDto?> GetByIdAsync(Guid id)
        {
            return await _context.Responsibilities
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => new ResponsibilityDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description
                })
                .FirstOrDefaultAsync();
        }

        public async Task<ResponsibilityDto> CreateAsync(CreateResponsibilityRequest request)
        {
            var responsibility = new Responsibility
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description
            };

            _context.Responsibilities.Add(responsibility);
            await _context.SaveChangesAsync();

            return new ResponsibilityDto
            {
                Id = responsibility.Id,
                Name = responsibility.Name,
                Description = responsibility.Description
            };
        }

        public async Task<bool> UpdateAsync(Guid id, UpdateResponsibilityRequest request)
        {
            var responsibility = await _context.Responsibilities.FindAsync(id);
            if (responsibility == null)
            {
                return false;
            }

            responsibility.Name = request.Name;
            responsibility.Description = request.Description;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var responsibility = await _context.Responsibilities.FindAsync(id);
            if (responsibility == null)
            {
                return false;
            }

            _context.Responsibilities.Remove(responsibility);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
