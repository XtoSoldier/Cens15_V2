using System.Security.Cryptography;
using System.Text;

namespace CENS15_V2.Security;

public static class CredentialVersion
{
    public const string Claim = "credential_version";

    public static string Compute(Guid userId, string passwordHash, string signingKey) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey),
            Encoding.UTF8.GetBytes($"CENS15:credential-version:v1:{userId:D}:{passwordHash}")));
}
