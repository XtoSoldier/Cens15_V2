using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AuthDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class AuthService: IAuthService
    {
        private readonly AppDbContext _context;
        private readonly ITokenService _tokenService;
        private readonly PasswordHasher _hasher;

        public AuthService(AppDbContext context, ITokenService token, PasswordHasher hasher)
        {
            _context = context;
            _tokenService = token;
            _hasher = hasher;
        }

        public async Task<AuthResponse?> Login(LoginRequest request)
        {
            var auth = await _context.Auths
                .Include(a => a.User)
                .ThenInclude(u => u.Role)
                .FirstOrDefaultAsync(x => x.Email == request.Email);

            if (auth == null)
                return null;

            if (!_hasher.Verify(request.Password, auth.PasswordHash))
                return null;

            return _tokenService.CreateToken(auth);
        }

        public async Task<AuthResponse> Register(RegisterRequest request)
        {
            if (await _context.Auths.AnyAsync(x => x.Email == request.Email))
                throw new Exception("El email ya existe");

            /// buscar rol default
            var role = await _context.Roles
                .FirstOrDefaultAsync(r => r.Name == "User");

            if (role == null)
                throw new Exception("No existe rol base");

            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = request.Nombres,
                LastName = request.Apellido,
                RoleId = role.Id,
                Status = true
            };

            var auth = new Auth
            {
                Id = user.Id,
                Email = request.Email,
                PasswordHash = _hasher.Hash(request.Password),
                User = user
            };

            _context.Users.Add(user);
            _context.Auths.Add(auth);

            await _context.SaveChangesAsync();

            return _tokenService.CreateToken(auth);
        }
    }
}
