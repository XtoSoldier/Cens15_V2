using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AuthDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace CENS15_V2.Services
{
    public class AuthService: IAuthService
    {
        private readonly AppDbContext _context;
        private readonly ITokenService _tokenService;
        private readonly PasswordHasher _hasher;
        private readonly IEmailService _emailService;

        public AuthService(AppDbContext context, ITokenService token, PasswordHasher hasher, IEmailService emailService)
        {
            _context = context;
            _tokenService = token;
            _hasher = hasher;
            _emailService = emailService;
        }

        public async Task<AuthResponse?> Login(LoginRequest request)
        {
            var auth = await _context.Auths
                .Include(a => a.User)
                .ThenInclude(u => u.Role)
                .ThenInclude(r => r.Responsibilities)
                .ThenInclude(rr => rr.Responsibility)
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
            var role = request.RoleId.HasValue
                ? await _context.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId.Value)
                : await _context.Roles.FirstOrDefaultAsync(r => r.Name == "User");

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
                PasswordHash = _hasher.Hash(string.IsNullOrWhiteSpace(request.Password) ? GenerateTemporaryPassword() : request.Password),
                MustChangePassword = string.IsNullOrWhiteSpace(request.Password),
                User = user
            };

            _context.Users.Add(user);
            _context.Auths.Add(auth);

            await _context.SaveChangesAsync();

            return _tokenService.CreateToken(auth);
        }

        public async Task RequestInitialAccessAsync(InitialAccessRequest request)
        {
            var email = request.Email.Trim();
            var auth = await _context.Auths
                .Include(a => a.User)
                .FirstOrDefaultAsync(x => x.Email.ToLower() == email.ToLower());

            if (auth == null || !auth.User.Status)
            {
                return;
            }

            var temporaryPassword = GenerateTemporaryPassword();
            await _emailService.SendInitialAccessAsync(auth, temporaryPassword);

            auth.PasswordHash = _hasher.Hash(temporaryPassword);
            auth.MustChangePassword = true;
            auth.InitialAccessSentAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }

        public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
        {
            var auth = await _context.Auths.FindAsync(userId);
            if (auth == null)
            {
                return false;
            }

            if (!_hasher.Verify(request.CurrentPassword, auth.PasswordHash))
            {
                return false;
            }

            if (request.NewPassword.Length < 6)
            {
                throw new InvalidOperationException("La nueva contraseña debe tener al menos 6 caracteres.");
            }

            auth.PasswordHash = _hasher.Hash(request.NewPassword);
            auth.MustChangePassword = false;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task RequestPasswordResetAsync(PasswordResetRequest request)
        {
            var email = request.Email.Trim();
            var auth = await _context.Auths
                .Include(a => a.User)
                .FirstOrDefaultAsync(x => x.Email.ToLower() == email.ToLower());

            if (auth == null || !auth.User.Status)
            {
                return;
            }

            var pendingCodes = await _context.PasswordResetCodes
                .Where(c => c.AuthId == auth.Id && c.UsedAt == null && c.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();
            foreach (var pendingCode in pendingCodes)
            {
                pendingCode.UsedAt = DateTime.UtcNow;
            }

            var code = GenerateVerificationCode();
            _context.PasswordResetCodes.Add(new PasswordResetCode
            {
                AuthId = auth.Id,
                CodeHash = _hasher.Hash(code),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15),
                Attempts = 0
            });

            await _emailService.SendPasswordResetCodeAsync(auth, code);
            await _context.SaveChangesAsync();
        }

        public async Task ValidatePasswordResetCodeAsync(ValidatePasswordResetCodeRequest request)
        {
            await ValidateResetCodeAsync(request.Email, request.Code, incrementAttempts: true);
        }

        public async Task ResetPasswordAsync(ResetPasswordRequest request)
        {
            if (request.NewPassword.Length < 6)
            {
                throw new InvalidOperationException("La nueva contraseña debe tener al menos 6 caracteres.");
            }

            var (auth, code) = await ValidateResetCodeAsync(request.Email, request.Code, incrementAttempts: true);

            code.UsedAt = DateTime.UtcNow;
            auth.PasswordHash = _hasher.Hash(request.NewPassword);
            auth.MustChangePassword = false;
            await _context.SaveChangesAsync();
        }

        private async Task<(Auth Auth, PasswordResetCode Code)> ValidateResetCodeAsync(string email, string codeValue, bool incrementAttempts)
        {
            var normalizedEmail = email.Trim();
            var auth = await _context.Auths
                .FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail.ToLower());

            if (auth == null)
            {
                throw new InvalidOperationException("No hay un código vigente para ese correo.");
            }

            var code = await _context.PasswordResetCodes
                .Where(c => c.AuthId == auth.Id && c.UsedAt == null)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();

            if (code == null)
            {
                throw new InvalidOperationException("No hay un código vigente para ese correo.");
            }

            if (code.ExpiresAt <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("El código está vencido. Solicitá uno nuevo.");
            }

            if (code.Attempts >= 5)
            {
                throw new InvalidOperationException("Se agotaron los intentos. Solicitá un nuevo código.");
            }

            if (!_hasher.Verify(codeValue.Trim(), code.CodeHash))
            {
                if (incrementAttempts)
                {
                    code.Attempts += 1;
                    await _context.SaveChangesAsync();
                    if (code.Attempts >= 5)
                    {
                        throw new InvalidOperationException("Se agotaron los intentos. Solicitá un nuevo código.");
                    }
                }
                throw new InvalidOperationException("El código ingresado es incorrecto.");
            }

            return (auth, code);
        }

        private static string GenerateTemporaryPassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
            return new string(Enumerable.Range(0, 10)
                .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)])
                .ToArray());
        }

        private static string GenerateVerificationCode()
        {
            return RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        }
    }
}
