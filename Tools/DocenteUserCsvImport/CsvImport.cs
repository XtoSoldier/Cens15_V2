using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;

namespace DocenteUserCsvImport;

public record SourceRow(int Line, string LastName, string FirstName, string Email);
public record Diagnostic(string Code, int[] Lines);
public record CsvSource(string Sha256, int TotalRows, List<SourceRow> Rows,
    List<Diagnostic> Errors, List<Diagnostic> Duplicates, List<Diagnostic> Warnings);
public record ExistingUser(Guid Id, Guid RoleId);
public record ExistingAuth(Guid Id, string Email);
public record ExistingRole(Guid Id, string Name);
public record ImportPlan(Guid? RoleId, bool WillCreateRole, List<SourceRow> Additions,
    List<int> MatchedLines, List<Diagnostic> Conflicts);

public static class CsvImport
{
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static CsvSource Read(byte[] bytes)
    {
        var sha = Hash(bytes);
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        var text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        var rows = new List<SourceRow>();
        var errors = new List<Diagnostic>();
        var duplicates = new List<Diagnostic>();
        var warnings = new List<Diagnostic>();
        var culture = CultureInfo.GetCultureInfo("es-AR");
        using var reader = new StringReader(text);
        var line = 0;
        while (reader.ReadLine() is { } raw)
        {
            line++;
            var fields = raw.Split(';');
            var names = fields[0].Split(',');
            if (fields.Length != 2 || names.Length != 2 || names.Any(n => string.IsNullOrWhiteSpace(n)))
            {
                errors.Add(new("InvalidRow", [line]));
                continue;
            }
            var email = fields[1].Trim();
            if (!MailAddress.TryCreate(email, out var address) || address.Address != email ||
                email.Count(c => c == '@') != 1 || email.IndexOf('@') <= 0 || email.Any(char.IsWhiteSpace))
            {
                errors.Add(new("InvalidEmail", [line]));
                continue;
            }
            // BCrypt only accepts 72 UTF-8 bytes; never silently truncate the required credential.
            if (Encoding.UTF8.GetByteCount(email[..email.IndexOf('@')]) > 72)
            {
                errors.Add(new("CredentialTooLong", [line]));
                continue;
            }
            var last = names[0].Trim().ToLower(culture);
            last = char.ToUpper(last[0], culture) + last[1..];
            rows.Add(new(line, last, names[1].Trim(), email));
        }
        if (line == 0) errors.Add(new("EmptySource", []));
        foreach (var group in rows.GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var lines = group.Select(r => r.Line).ToArray();
            if (group.Select(r => (r.LastName, r.FirstName)).Distinct().Count() != 1)
                errors.Add(new("SameEmailDifferentNames", lines));
            else duplicates.Add(new("RepeatedEmailAndName", lines));
        }
        var unique = rows.DistinctBy(r => r.Email, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var group in unique.GroupBy(r => (r.LastName, r.FirstName)).Where(g => g.Count() > 1))
        {
            var emails = group.Select(r => r.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
            warnings.Add(new("SameNameDifferentEmails", rows.Where(r => emails.Contains(r.Email)).Select(r => r.Line).ToArray()));
        }
        return new(sha, line, unique, errors, duplicates, warnings);
    }

    public static ImportPlan Plan(CsvSource source, IReadOnlyList<ExistingUser> users,
        IReadOnlyList<ExistingAuth> auths, IReadOnlyList<ExistingRole> roles)
    {
        var docente = roles.Where(r => string.Equals(r.Name.Trim(), "Docente", StringComparison.OrdinalIgnoreCase)).ToList();
        var conflicts = new List<Diagnostic>();
        if (docente.Count > 1) conflicts.Add(new("AmbiguousDocenteRole", []));
        var roleId = docente.Count == 1 ? (Guid?)docente[0].Id : null;
        var additions = new List<SourceRow>();
        var matches = new List<int>();
        var usersById = users.ToDictionary(u => u.Id);
        var byEmail = auths.ToLookup(a => a.Email, StringComparer.OrdinalIgnoreCase);
        foreach (var row in source.Rows)
        {
            var existing = byEmail[row.Email].ToList();
            if (existing.Count == 0) additions.Add(row);
            else if (existing.Count != 1 || !usersById.TryGetValue(existing[0].Id, out var user) || user.RoleId != roleId)
                conflicts.Add(new("ExistingEmailRoleOrIdentityConflict", [row.Line]));
            else matches.Add(row.Line);
        }
        return new(roleId, docente.Count == 0, additions, matches, conflicts);
    }

    public static User Map(SourceRow row, Guid roleId, PasswordHasher hasher)
    {
        var id = Guid.NewGuid();
        var user = new User { Id = id, FirstName = row.FirstName, LastName = row.LastName,
            RoleId = roleId, Status = true };
        user.Auth = new Auth { Id = id, Email = row.Email, MustChangePassword = false,
            PasswordHash = hasher.Hash(row.Email[..row.Email.IndexOf('@')]), User = user };
        return user;
    }
}
