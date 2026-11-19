using System.Globalization;
using System.Text;

namespace DocenteUserCsvImport;

public sealed record LinkDocente(int Id, string Nombres, string Apellidos, string? Email, Guid? UserId);
public sealed record LinkUser(Guid Id, string FirstName, string LastName, bool Status, Guid RoleId);
public sealed record LinkAuth(Guid Id, string? Email);
public sealed record LinkProposal(int DocenteId, string DocenteName, Guid UserId, string Method);
public sealed record LinkSkipped(int DocenteId, string DocenteName, string Reason);
public sealed record LinkPlan(IReadOnlyList<LinkProposal> Proposals, IReadOnlyList<LinkSkipped> Skipped);

public static class DocenteUserLink
{
    private static string Normalize(string value) => string.Join(" ", new string(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
        .Normalize(NormalizationForm.FormC).ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Name(string first, string last) =>
        string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last) ? "" : Normalize(first + " " + last);

    public static LinkPlan Plan(IReadOnlyList<LinkDocente> docentes, IReadOnlyList<LinkUser> users,
        IReadOnlyList<LinkAuth> auths, IReadOnlyList<ExistingRole> roles)
    {
        var proposals = new List<LinkProposal>();
        var skipped = new List<LinkSkipped>();
        var occupied = docentes.Where(d => d.UserId.HasValue).Select(d => d.UserId!.Value).ToHashSet();
        var docenteRoles = roles.Where(r => string.Equals(r.Name.Trim(), "Docente", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Id).ToHashSet();
        string? Ineligible(LinkUser u) => !u.Status ? "InactiveUser" : !docenteRoles.Contains(u.RoleId) ? "NonDocenteRole" :
            auths.Count(a => a.Id == u.Id) != 1 ? "MissingOrAmbiguousAuth" : null;

        foreach (var d in docentes.OrderBy(d => d.Id))
        {
            var display = (d.Nombres + " " + d.Apellidos).Trim();
            void Skip(string reason) => skipped.Add(new(d.Id, display, reason));
            if (d.UserId.HasValue) { Skip("ExistingLinkPreserved"); continue; }
            var name = Name(d.Nombres, d.Apellidos);
            var emailMatches = string.IsNullOrWhiteSpace(d.Email) ? [] : auths.Where(a =>
                string.Equals(a.Email?.Trim(), d.Email.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (emailMatches.Length > 1) { Skip("AmbiguousEmail"); continue; }
            LinkUser? user;
            var method = "ExactEmailAndName";
            if (emailMatches.Length == 1)
            {
                user = users.SingleOrDefault(u => u.Id == emailMatches[0].Id);
                if (user is null) { Skip("AuthWithoutUser"); continue; }
                if (Ineligible(user) is { } reason) { Skip(reason); continue; }
                if (name.Length == 0 || name != Name(user.FirstName, user.LastName))
                { Skip("EmailNameConflict"); continue; }
            }
            else
            {
                method = "ExactNormalizedName";
                var named = name.Length == 0 ? [] : users.Where(u => Name(u.FirstName, u.LastName) == name).ToArray();
                var eligible = named.Where(u => Ineligible(u) is null).ToArray();
                if (eligible.Length > 1) { Skip("AmbiguousName"); continue; }
                if (eligible.Length == 0)
                {
                    Skip(named.Length == 1 ? Ineligible(named[0])! : "NoEligibleMatch");
                    continue;
                }
                user = eligible[0];
            }
            if (occupied.Contains(user.Id)) { Skip("UserAlreadyLinked"); continue; }
            proposals.Add(new(d.Id, display, user.Id, method));
        }

        // Resolve contention only after collecting every candidate; iteration order must never pick a winner.
        var contested = proposals.GroupBy(p => p.UserId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var p in proposals.Where(p => contested.Contains(p.UserId)))
            skipped.Add(new(p.DocenteId, p.DocenteName, "MultipleDocentesForUser"));
        return new(proposals.Where(p => !contested.Contains(p.UserId)).OrderBy(p => p.DocenteId).ToArray(),
            skipped.OrderBy(s => s.DocenteId).ToArray());
    }
}
