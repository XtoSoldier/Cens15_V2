using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.RolesDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class RoleService : IRoleService
    {
        private readonly AppDbContext _context;

        public RoleService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<RoleDto>> GetAllAsync()
        {
            return await _context.Roles
                .AsNoTracking()
                .Include(r => r.Responsibilities)
                .Select(r => new RoleDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    ResponsibilityIds = r.Responsibilities
                        .Select(rr => rr.ResponsibilityId)
                        .ToList()
                })
                .ToListAsync();
        }

        public async Task<RoleDto?> GetByIdAsync(Guid id)
        {
            return await _context.Roles
                .AsNoTracking()
                .Include(r => r.Responsibilities)
                .Where(r => r.Id == id)
                .Select(r => new RoleDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    ResponsibilityIds = r.Responsibilities
                        .Select(rr => rr.ResponsibilityId)
                        .ToList()
                })
                .FirstOrDefaultAsync();
        }

        public async Task<RoleDto> CreateAsync(CreateRoleRequest request)
        {
            var role = new Role
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Responsibilities = new List<RoleResponsibility>()
            };

            if (request.ResponsibilityIds != null && request.ResponsibilityIds.Count > 0)
            {
                foreach (var responsibilityId in request.ResponsibilityIds.Distinct())
                {
                    role.Responsibilities.Add(new RoleResponsibility
                    {
                        RoleId = role.Id,
                        ResponsibilityId = responsibilityId
                    });
                }
            }

            _context.Roles.Add(role);
            await _context.SaveChangesAsync();

            return new RoleDto
            {
                Id = role.Id,
                Name = role.Name,
                ResponsibilityIds = role.Responsibilities.Select(rr => rr.ResponsibilityId).ToList()
            };
        }

        public async Task<bool> UpdateAsync(Guid id, UpdateRoleRequest request)
        {
            var role = await _context.Roles
                .Include(r => r.Responsibilities)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (role == null)
            {
                return false;
            }

            role.Name = request.Name;

            var currentRelations = role.Responsibilities.ToList();
            _context.RemoveRange(currentRelations);

            var newResponsibilityIds = request.ResponsibilityIds?.Distinct().ToList() ?? new List<Guid>();
            foreach (var responsibilityId in newResponsibilityIds)
            {
                role.Responsibilities.Add(new RoleResponsibility
                {
                    RoleId = role.Id,
                    ResponsibilityId = responsibilityId
                });
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var role = await _context.Roles.FindAsync(id);
            if (role == null)
            {
                return false;
            }

            _context.Roles.Remove(role);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
