using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.ResponsibilitiesDTOs;
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

            var newResponsibilityIds = request.ResponsibilityIds?.Distinct().ToList() ?? new List<Guid>();
            await EnsureResponsibilitiesExistAsync(newResponsibilityIds);

            foreach (var responsibilityId in newResponsibilityIds)
            {
                role.Responsibilities.Add(new RoleResponsibility
                {
                    RoleId = role.Id,
                    ResponsibilityId = responsibilityId
                });
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

            var ids = request.ResponsibilityIds?.Distinct().ToList() ?? new List<Guid>();
            await EnsureResponsibilitiesExistAsync(ids);
            ReplaceResponsibilities(role, ids);

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

        public async Task<bool> AssignResponsibilitiesAsync(Guid roleId, AssignResponsibilitiesRequest request)
        {
            var role = await _context.Roles
                .Include(r => r.Responsibilities)
                .FirstOrDefaultAsync(r => r.Id == roleId);

            if (role == null)
            {
                return false;
            }

            var ids = request.ResponsibilityIds.Distinct().ToList();
            await EnsureResponsibilitiesExistAsync(ids);
            ReplaceResponsibilities(role, ids);

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<ResponsibilityDto>> GetResponsibilitiesAsync(Guid roleId)
        {
            var roleExists = await _context.Roles.AnyAsync(r => r.Id == roleId);
            if (!roleExists)
            {
                return Enumerable.Empty<ResponsibilityDto>();
            }

            return await _context.Set<RoleResponsibility>()
                .AsNoTracking()
                .Where(rr => rr.RoleId == roleId)
                .Select(rr => new ResponsibilityDto
                {
                    Id = rr.Responsibility.Id,
                    Name = rr.Responsibility.Name,
                    Description = rr.Responsibility.Description
                })
                .ToListAsync();
        }

        private async Task EnsureResponsibilitiesExistAsync(ICollection<Guid> ids)
        {
            if (ids.Count == 0)
            {
                return;
            }

            var existingCount = await _context.Responsibilities.CountAsync(r => ids.Contains(r.Id));
            if (existingCount != ids.Count)
            {
                throw new InvalidOperationException("Una o más responsabilidades no existen.");
            }
        }

        private void ReplaceResponsibilities(Role role, ICollection<Guid> responsibilityIds)
        {
            var currentRelations = role.Responsibilities.ToList();
            _context.RemoveRange(currentRelations);
            role.Responsibilities = responsibilityIds
                .Select(id => new RoleResponsibility
                {
                    RoleId = role.Id,
                    ResponsibilityId = id
                })
                .ToList();
        }
    }
}