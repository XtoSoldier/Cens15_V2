using System.Text.Json;
using DocenteUserCsvImport;
using Xunit;

namespace CENS15_V2.Tests;

public class DocenteUserLinkTests
{
    private static readonly Guid RoleId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly ExistingRole[] Roles = [new(RoleId, " Docente ")];
    private static LinkDocente D(int id = 1, string? email = "match@example.test", Guid? userId = null) =>
        new(id, "Ana Maria", "Perez", email, userId);
    private static LinkUser U(Guid? id = null, bool active = true, Guid? role = null) =>
        new(id ?? UserId, "Ana Maria", "Perez", active, role ?? RoleId);
    private static LinkPlan Plan(LinkDocente[] docentes, LinkUser[]? users = null, LinkAuth[]? auths = null) =>
        DocenteUserLink.Plan(docentes, users ?? [U()], auths ?? [new(UserId, "match@example.test")], Roles);

    [Fact]
    public void ExactEmailTrimsAndIgnoresCaseAndVerifiesNormalizedNames()
    {
        var plan = Plan([D() with { Nombres = "  ANA  Mar\u00eda ", Apellidos = " P\u00e9rez ", Email = " MATCH@EXAMPLE.TEST " }]);
        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal(UserId, proposal.UserId);
        Assert.Equal("ExactEmailAndName", proposal.Method);
        Assert.Empty(plan.Skipped);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-matching@example.test")]
    public void UniqueFullNameFallbackWhenEmailDoesNotMatch(string? email) =>
        Assert.Equal("ExactNormalizedName", Assert.Single(Plan([D(email: email)]).Proposals).Method);

    [Fact]
    public void UniqueEmailWithContradictoryNamesNeverFallsBack()
    {
        var plan = Plan([D()], [U() with { FirstName = "Different" }, U(OtherId)],
            [new(UserId, "match@example.test"), new(OtherId, "other@example.test")]);
        Assert.Empty(plan.Proposals);
        Assert.Equal("EmailNameConflict", Assert.Single(plan.Skipped).Reason);
    }

    [Fact]
    public void NamesAreFullExactNotFuzzyOrReordered()
    {
        Assert.Equal("NoEligibleMatch", Assert.Single(Plan([D(email: null) with { Nombres = "Ana" }]).Skipped).Reason);
        Assert.Equal("EmailNameConflict", Assert.Single(Plan([D() with { Nombres = "", Apellidos = "" }]).Skipped).Reason);
        Assert.Empty(Plan([D(email: null) with { Nombres = "Perez", Apellidos = "Ana Maria" }]).Proposals);
    }

    [Fact]
    public void ExistingLegacyLinksArePreservedEvenForInactiveAdminWithoutAuth()
    {
        var d = D(userId: UserId);
        var plan = Plan([d], [U(active: false, role: OtherId)], []);
        Assert.Empty(plan.Proposals);
        Assert.Equal("ExistingLinkPreserved", Assert.Single(plan.Skipped).Reason);
        Assert.Equal(UserId, d.UserId);
    }

    [Fact]
    public void OccupancyIsCheckedAcrossAllDocentesNotJustMatchingCandidates()
    {
        var plan = Plan([D(), D(2, userId: UserId) with { Nombres = "Unrelated", Apellidos = "Legacy" }]);
        Assert.Empty(plan.Proposals);
        Assert.Equal("UserAlreadyLinked", plan.Skipped.Single(s => s.DocenteId == 1).Reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InactiveOrWrongRoleIsReportedWithoutFallback(bool inactive)
    {
        var plan = Plan([D()], [U(active: !inactive, role: inactive ? RoleId : OtherId)]);
        Assert.Empty(plan.Proposals);
        Assert.Equal(inactive ? "InactiveUser" : "NonDocenteRole", Assert.Single(plan.Skipped).Reason);
    }

    [Fact]
    public void MissingAuthAndOrphanAuthCannotLink()
    {
        Assert.Equal("MissingOrAmbiguousAuth", Assert.Single(Plan([D()], auths: []).Skipped).Reason);
        Assert.Equal("AuthWithoutUser", Assert.Single(Plan([D()], users: []).Skipped).Reason);
    }

    [Fact]
    public void AmbiguousEmailIsBlockedEvenWhenOnlyOneUserIsEligible()
    {
        var plan = Plan([D()], [U(), U(OtherId, active: false)],
            [new(UserId, "match@example.test"), new(OtherId, " MATCH@EXAMPLE.TEST ")]);
        Assert.Empty(plan.Proposals);
        Assert.Equal("AmbiguousEmail", Assert.Single(plan.Skipped).Reason);
    }

    [Fact]
    public void SameNameWithDifferentEmailsIsAmbiguousForFallbackButEmailHasPriority()
    {
        LinkUser[] users = [U(), U(OtherId)];
        LinkAuth[] auths = [new(UserId, "match@example.test"), new(OtherId, "other@example.test")];
        Assert.Equal("AmbiguousName", Assert.Single(Plan([D(email: null)], users, auths).Skipped).Reason);
        Assert.Equal(UserId, Assert.Single(Plan([D()], users, auths).Proposals).UserId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoDocentesClaimingOneUserAreBothExcludedWithoutWinner(bool bothEmail)
    {
        var plan = Plan([D(2, email: bothEmail ? "match@example.test" : null), D()]);
        Assert.Empty(plan.Proposals);
        Assert.Equal([1, 2], plan.Skipped.Select(s => s.DocenteId).ToArray());
        Assert.All(plan.Skipped, s => Assert.Equal("MultipleDocentesForUser", s.Reason));
    }

    [Fact]
    public void MatchingNeverUsesEmailPrefix()
    {
        var plan = Plan([D(email: "match@different.test") with { Nombres = "Other" }]);
        Assert.Empty(plan.Proposals);
        Assert.Equal("NoEligibleMatch", Assert.Single(plan.Skipped).Reason);
    }

    [Fact]
    public void PlanIsDeterministicAndFreshLinkedStateIsIdempotent()
    {
        LinkDocente[] docentes = [D(2, email: "other@example.test") with { Nombres = "Juan" }, D()];
        LinkUser[] users = [U(OtherId) with { FirstName = "Juan" }, U()];
        LinkAuth[] auths = [new(OtherId, "other@example.test"), new(UserId, "match@example.test")];
        var plan = Plan(docentes, users, auths);
        Assert.Equal([1, 2], plan.Proposals.Select(p => p.DocenteId).ToArray());
        Assert.Equal(JsonSerializer.Serialize(plan), JsonSerializer.Serialize(Plan(docentes.Reverse().ToArray(), users.Reverse().ToArray(), auths.Reverse().ToArray())));
        var linked = docentes.Select(d => d with { UserId = plan.Proposals.Single(p => p.DocenteId == d.Id).UserId }).ToArray();
        Assert.Empty(Plan(linked, users, auths).Proposals);
        var output = JsonSerializer.Serialize(plan);
        Assert.DoesNotContain("@", output);
        Assert.DoesNotContain("Password", output);
    }

    private static SortedDictionary<string, string[]> State(string? link = null, string password = "synthetic-secret", string name = "Ana", string role = "Docente") => new(StringComparer.Ordinal)
    {
        ["Docentes"] = [$"{{\"Id\":1,\"Nombres\":\"{name}\",\"UserId\":{(link is null ? "null" : "\"" + link + "\"")}}}"],
        ["Users"] = ["{\"Id\":\"u\",\"Status\":true,\"Image\":null}"],
        ["Auths"] = [$"{{\"Id\":\"u\",\"PasswordHash\":\"{password}\"}}"],
        ["Roles"] = [$"{{\"Id\":\"r\",\"Name\":\"{role}\"}}"]
    };

    [Fact]
    public void FullScalarFingerprintAllowsOnlyProposedNullToUserIdChanges()
    {
        LinkProposal[] links = [new(1, "Ana Perez", UserId, "ExactEmailAndName")];
        var expected = DocenteUserLinkCli.StateFingerprint(State(), links);
        var after = State(UserId.ToString());
        Assert.Equal(expected, DocenteUserLinkCli.StateFingerprint(after));
        Assert.NotEqual(expected, DocenteUserLinkCli.StateFingerprint(State(OtherId.ToString())));
        Assert.NotEqual(expected, DocenteUserLinkCli.StateFingerprint(State(UserId.ToString(), password: "changed")));
        Assert.NotEqual(expected, DocenteUserLinkCli.StateFingerprint(State(UserId.ToString(), name: "Changed")));
        Assert.NotEqual(expected, DocenteUserLinkCli.StateFingerprint(State(UserId.ToString(), role: "Admin")));
        Assert.Throws<InvalidOperationException>(() => DocenteUserLinkCli.StateFingerprint(after, links));
        Assert.DoesNotContain("synthetic-secret", expected);
        after["Users"] = ["{\"Id\":\"u\",\"Status\":false,\"Image\":null}"];
        Assert.NotEqual(expected, DocenteUserLinkCli.StateFingerprint(after));
    }

    [Fact]
    public void FingerprintIsStableAcrossRowAndPropertyOrderAndDetectsUnmappedScalars()
    {
        var first = State();
        first["Users"] = ["{\"Id\":1,\"Unmapped\":true}", "{\"Id\":2,\"Unmapped\":false}"];
        var second = State();
        second["Users"] = ["{\"Unmapped\":false,\"Id\":2}", "{\"Unmapped\":true,\"Id\":1}"];
        Assert.Equal(DocenteUserLinkCli.StateFingerprint(first), DocenteUserLinkCli.StateFingerprint(second));
        second["Users"] = ["{\"Unmapped\":false,\"Id\":2}", "{\"Unmapped\":false,\"Id\":1}"];
        Assert.NotEqual(DocenteUserLinkCli.StateFingerprint(first), DocenteUserLinkCli.StateFingerprint(second));
    }

    [Theory]
    [InlineData("--apply")]
    [InlineData("--unknown")]
    public async Task DispatchRejectsUnsafeArgumentsBeforeAccessingDatabase(string argument) =>
        Assert.Equal(1, await ImportCli.Main(["--link-docentes", argument]));

    [Fact]
    public async Task ApplyRequiresBothConfirmationsAndRejectsDuplicateOptions()
    {
        Assert.Equal(1, await ImportCli.Main(["--link-docentes", "--apply", "--expected-database", "cens15"]));
        Assert.Equal(1, await ImportCli.Main(["--link-docentes", "--apply", "--expected-receipt", "digest"]));
        Assert.Equal(1, await ImportCli.Main(["--link-docentes", "--apply", "--apply"]));
        Assert.Equal(1, await ImportCli.Main(["--link-docentes", "--expected-database", "cens15", "--expected-database", "cens15"]));
    }
}
