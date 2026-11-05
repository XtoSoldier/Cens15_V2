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

            if (auth == null || auth.User == null || !auth.User.Status)
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

        public Task RequestInitialAccessAsync(InitialAccessRequest request) =>
            RequestPasswordResetAsync(new PasswordResetRequest { Email = request.Email });

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
            await using var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync() : null;
            var auth = await GetActiveAuthForUpdateAsync(request.Email);
            if (auth == null) return;
            var code = GenerateVerificationCode();
            var codeHash = _hasher.Hash(code);
            try
            {
                await _emailService.SendPasswordResetCodeAsync(auth, code);
            }
            catch (Exception)
            {
                // Provider responses may contain addresses or credentials. Do not expose them.
                throw new InvalidOperationException("No se pudo enviar el codigo. Intenta nuevamente.");
            }

            var now = DateTime.UtcNow;
            var pendingCodes = await _context.PasswordResetCodes
                .Where(c => c.AuthId == auth.Id && c.UsedAt == null)
                .ToListAsync();
            foreach (var pendingCode in pendingCodes) pendingCode.UsedAt = now;
            _context.PasswordResetCodes.Add(new PasswordResetCode
            {
                AuthId = auth.Id,
                CodeHash = codeHash,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(15),
                Attempts = 0
            });

            await _context.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();
        }

        public async Task ValidatePasswordResetCodeAsync(ValidatePasswordResetCodeRequest request)
        {
            await ValidateResetCodeAsync(request.Email, request.Code);
        }

        public async Task ResetPasswordAsync(ResetPasswordRequest request)
        {
            if (request.NewPassword.Length < 6)
            {
                throw new InvalidOperationException("La nueva contraseña debe tener al menos 6 caracteres.");
            }

            await ValidateResetCodeAsync(request.Email, request.Code, request.NewPassword);
        }

        private async Task<Auth?> GetActiveAuthForUpdateAsync(string email)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            // Begin the caller's transaction before this query. All OTP operations lock the same row.
            var query = _context.Database.IsRelational()
                ? _context.Auths.FromSqlInterpolated($"SELECT * FROM \"Auths\" WHERE lower(\"Email\") = {normalizedEmail} FOR UPDATE")
                : _context.Auths.Where(a => a.Email.ToLower() == normalizedEmail);
            var auth = await query.SingleOrDefaultAsync();
            if (auth == null) return null;
            await _context.Entry(auth).Reference(a => a.User).LoadAsync();
            return auth.User?.Status == true ? auth : null;
        }

        private async Task ValidateResetCodeAsync(string email, string codeValue, string? newPassword = null)
        {
            await using var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync() : null;
            var auth = await GetActiveAuthForUpdateAsync(email);
            if (auth == null)
            {
                throw new InvalidOperationException("No hay un código vigente para ese correo.");
            }

            var code = await _context.PasswordResetCodes
                .Where(c => c.AuthId == auth.Id)
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .FirstOrDefaultAsync();

            if (code == null || code.UsedAt != null)
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

            if (codeValue.Length != 6 || codeValue.Any(c => c < '0' || c > '9') || !_hasher.Verify(codeValue, code.CodeHash))
            {
                code.Attempts += 1;
                await _context.SaveChangesAsync();
                // A rejected request must still persist its attempt, not roll it back on disposal.
                if (transaction != null) await transaction.CommitAsync();
                if (code.Attempts >= 5)
                {
                    throw new InvalidOperationException("Se agotaron los intentos. Solicitá un nuevo código.");
                }
                throw new InvalidOperationException("El código ingresado es incorrecto.");
            }

            if (newPassword != null)
            {
                var unusedCodes = await _context.PasswordResetCodes
                    .Where(c => c.AuthId == auth.Id && c.UsedAt == null).ToListAsync();
                var now = DateTime.UtcNow;
                foreach (var unusedCode in unusedCodes) unusedCode.UsedAt = now;
                auth.PasswordHash = _hasher.Hash(newPassword);
                auth.MustChangePassword = false;
                await _context.SaveChangesAsync();
            }
            if (transaction != null) await transaction.CommitAsync();
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
