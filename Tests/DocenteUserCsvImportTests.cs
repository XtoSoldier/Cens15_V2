using System.Text;
using CENS15_V2.Helper;
using DocenteUserCsvImport;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CENS15_V2.Tests;

public class DocenteUserCsvImportTests
{
    private static CsvSource Read(string text) => CsvImport.Read(Encoding.UTF8.GetBytes(text));
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly ExistingRole[] Roles = [new(RoleId, " Docente ")];

    [Fact]
    public void ParserNormalizesOnlySurnameAndPreservesLiteralNamesAndEmail()
    {
        var row = Assert.Single(Read("  SANCHEZ MAMANÍ ,  mArIa José  ; Rocioejuarez4022@example.test  ").Rows);
        Assert.Equal("Sanchez mamaní", row.LastName);
        Assert.Equal("mArIa José", row.FirstName);
        Assert.Equal("Rocioejuarez4022@example.test", row.Email);
        Assert.Equal(1, row.Line);
    }

    [Fact]
    public void Utf8BomIsAcceptedAndIncludedInSourceHash()
    {
        var bytes = Encoding.UTF8.GetBytes("PEREZ, José;user@example.test\r\n");
        var withBom = new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray();
        Assert.Single(CsvImport.Read(withBom).Rows);
        Assert.NotEqual(CsvImport.Read(bytes).Sha256, CsvImport.Read(withBom).Sha256);
        Assert.Equal(1, CsvImport.Read(withBom).TotalRows);
    }

    [Fact]
    public void InvalidUtf8IsRejectedWithoutReplacement()
    {
        Assert.Throws<DecoderFallbackException>(() => CsvImport.Read([0xff, 0xfe, 0x41]));
        Assert.Throws<DecoderFallbackException>(() => CsvImport.Read([0xc3, 0x28]));
    }

    [Fact]
    public void EmptyFileBlocksEvenRoleOnlyApply()
    {
        var source = CsvImport.Read([]);
        Assert.Equal("EmptySource", Assert.Single(source.Errors).Code);
        Assert.Empty(source.Rows);
    }

    [Theory]
    [InlineData(72, true)]
    [InlineData(73, false)]
    public void BcryptByteLimitCannotSilentlyTruncateCredential(int length, bool valid)
    {
        var source = Read($"Perez, Ana;{new string('a', length)}@example.test");
        Assert.Equal(valid, source.Errors.Count == 0);
        Assert.Equal(valid ? 1 : 0, source.Rows.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("APELLIDO, Nombre;email")]
    [InlineData("Perez;user@example.test")]
    [InlineData("Perez, ;user@example.test")]
    [InlineData("Perez, Ana;Display <user@example.test>")]
    [InlineData("Perez, Ana;user@example.test;extra")]
    [InlineData("Perez, Ana;user@@example.test")]
    public void InvalidRowsDoNotExposeSourceInDiagnostics(string row)
    {
        var result = Read(row + "\n");
        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
        Assert.Equal([1], result.Errors[0].Lines);
        Assert.DoesNotContain("example.test", System.Text.Json.JsonSerializer.Serialize(result.Errors));
    }

    [Fact]
    public void RepeatedEmailUsesFirstLiteralAndRequiresIdenticalNormalizedNames()
    {
        var result = Read("PEREZ, Ana;User@example.test\nperez, Ana;user@EXAMPLE.test");
        Assert.Empty(result.Errors);
        Assert.Equal("User@example.test", Assert.Single(result.Rows).Email);
        Assert.Equal([1, 2], Assert.Single(result.Duplicates).Lines);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void SameEmailWithDifferentNamesBlocks()
    {
        var result = Read("Perez, Ana;user@example.test\nPerez, ANA;USER@example.test");
        Assert.Equal("SameEmailDifferentNames", Assert.Single(result.Errors).Code);
        Assert.Empty(result.Duplicates);
    }

    [Fact]
    public void SameNameDifferentEmailsPreservesBothAndWarnsWithAllLines()
    {
        var result = Read("MERELES, Juana Luján;first@example.test\nMERELES, Juana Luján;second@example.test\nmereles, Juana Luján;FIRST@example.test");
        Assert.Equal(2, result.Rows.Count);
        Assert.Empty(result.Errors);
        Assert.Equal([1, 2, 3], Assert.Single(result.Warnings).Lines);
    }

    [Fact]
    public void MapperUsesExistingHasherAndSharedGuidWithoutOtherEntities()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Security:BCryptWorkFactor"] = "4" }).Build();
        var hasher = new PasswordHasher(config);
        var row = Assert.Single(Read("PEREZ, Ana;Rocioejuarez4022@example.test").Rows);
        var user = CsvImport.Map(row, RoleId, hasher);
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(user.Id, user.Auth.Id);
        Assert.Same(user, user.Auth.User);
        Assert.Equal(RoleId, user.RoleId);
        Assert.Equal(row.FirstName, user.FirstName);
        Assert.Equal(row.LastName, user.LastName);
        Assert.True(user.Status);
        Assert.False(user.Auth.MustChangePassword);
        Assert.True(hasher.Verify("Rocioejuarez4022", user.Auth.PasswordHash));
        Assert.False(hasher.Verify("rocioejuarez4022", user.Auth.PasswordHash));
        Assert.Null(user.Auth.InitialAccessSentAt);
        Assert.Null(user.Auth.Token);
        Assert.Null(user.Docente);
        Assert.Null(user.Preceptor);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void ExplicitLoopbackOnly(string host)
    {
        var target = ImportCli.LocalTarget($"Host={host};Database=local_test;Username=test;Include Error Detail=true;Log Parameters=true");
        Assert.Equal("local_test", target.Database);
        Assert.False(target.IncludeErrorDetail);
        Assert.False(target.LogParameters);
    }

    [Theory]
    [InlineData("Host=production;Database=test")]
    [InlineData("Host=localhost,production;Database=test")]
    [InlineData("Host=127.0.0.2;Database=test")]
    [InlineData("Host=localhost")]
    public void UnsafeTargetsAreRejected(string connectionString) =>
        Assert.Throws<InvalidOperationException>(() => ImportCli.LocalTarget(connectionString));

    [Fact]
    public void PlanningIsInsertOnlyAndIdempotentRegardlessOfExistingNames()
    {
        var source = Read("Perez, Ana;User@example.test");
        var initial = CsvImport.Plan(source, [], [], Roles);
        Assert.Single(initial.Additions);
        Assert.False(initial.WillCreateRole);
        var id = Guid.NewGuid();
        var repeated = CsvImport.Plan(source, [new(id, RoleId)], [new(id, "USER@EXAMPLE.test")], Roles);
        Assert.Empty(repeated.Additions);
        Assert.Equal([1], repeated.MatchedLines);
        Assert.Empty(repeated.Conflicts);
    }

    [Fact]
    public void MissingRoleIsPlannedWithoutGrantingPermissions()
    {
        var plan = CsvImport.Plan(Read("Perez, Ana;user@example.test"), [], [], []);
        Assert.True(plan.WillCreateRole);
        Assert.Null(plan.RoleId);
        Assert.Single(plan.Additions);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void AmbiguousNormalizedRolesBlock()
    {
        var plan = CsvImport.Plan(Read("Perez, Ana;user@example.test"), [], [],
            [new(RoleId, "Docente"), new(Guid.NewGuid(), " DOCENTE ")]);
        Assert.Equal("AmbiguousDocenteRole", Assert.Single(plan.Conflicts).Code);
        Assert.False(plan.WillCreateRole);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void WrongRoleOrOrphanOrAmbiguousExistingEmailBlocks(bool hasUser, bool duplicate)
    {
        var id = Guid.NewGuid();
        ExistingUser[] users = hasUser ? [new(id, Guid.NewGuid())] : [];
        ExistingAuth[] auths = duplicate ? [new(id, "user@example.test"), new(Guid.NewGuid(), "USER@example.test")] : [new(id, "user@example.test")];
        var plan = CsvImport.Plan(Read("Perez, Ana;user@example.test"), users, auths, Roles);
        Assert.Empty(plan.Additions);
        Assert.Empty(plan.MatchedLines);
        Assert.Single(plan.Conflicts);
    }

    [Fact]
    public async Task ApplyRequiresAllConfirmationsBeforeAnyDatabaseAccess()
    {
        Assert.Equal(1, await ImportCli.Main(["--apply", "--file", "nonexistent.csv"]));
    }
}
