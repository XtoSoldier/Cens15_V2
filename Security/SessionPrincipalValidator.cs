using System.Security.Claims;
using CENS15_V2.Data;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Security;

public static class SessionPrincipalValidator
{
    public static async Task<bool> ValidateAsync(ClaimsPrincipal? principal, AppDbContext db,
        string currentEpoch, string jwtKey, CancellationToken ct = default, Action<string>? onRejected = null)
    {
        bool Reject(string reason) { onRejected?.Invoke(reason); return false; }
        if (principal?.Identity?.IsAuthenticated != true) return Reject("unauthenticated");
        // JWT mapping can produce both sub and NameIdentifier with the same value.
        var epochs = principal.FindAll(SessionEpoch.Claim).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();
        var ids = principal.FindAll(ClaimTypes.NameIdentifier).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();
        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();
        var versions = principal.FindAll(CredentialVersion.Claim).Select(c => c.Value).ToArray();
        var emails = principal.Claims.Where(c => c.Type is ClaimTypes.Email or "email").Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (epochs.Length != 1 || ids.Length != 1 || !Guid.TryParse(ids[0], out var id)
            || roles.Length != 1 || emails.Length != 1 || versions.Length != 1) return Reject("invalid_claims");
        if (epochs[0] != currentEpoch) return Reject("session_epoch_changed");

        var user = await db.Users.AsNoTracking().Include(u => u.Auth)
            .Include(u => u.Role).ThenInclude(r => r.Responsibilities).ThenInclude(rr => rr.Responsibility)
            .SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user == null || !user.Status || user.Auth == null || user.Role == null)
            return Reject("account_unavailable");
        if (versions[0] != CredentialVersion.Compute(user.Auth.Id, user.Auth.PasswordHash, jwtKey))
            return Reject("credential_changed");
        if (roles[0] != user.Role.Name || !string.Equals(emails[0], user.Auth.Email, StringComparison.OrdinalIgnoreCase))
            return Reject("identity_changed");

        if (!principal.FindAll(ResponsibilityPolicies.ClaimType).Select(c => c.Value).ToHashSet(StringComparer.Ordinal)
            .SetEquals(user.Role.Responsibilities.Select(rr => rr.Responsibility.Name)))
            return Reject("responsibilities_changed");
        return true;
    }
}
