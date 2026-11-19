# Docente User CSV Import

Standalone backend tool, .NET 8, with no new packages. It never starts the API,
runs migrations/seeds, sends mail, or creates domain Docentes, courses or tokens.
Run from the API root. Configuration is appsettings.json, optional
appsettings.Development.json, then environment variables. Production configuration
is never loaded. Only a named database on localhost, 127.0.0.1 or ::1 is allowed.
Output contains the safe host/port/database target, never connection credentials.

## Preview

```powershell
dotnet run --project Tools/DocenteUserCsvImport -c Release -- --file ../docentes.csv
```

`--file` is required; `--api-root` optionally selects the configuration directory.
Preview uses a serializable READ ONLY transaction and rolls it back. Exit codes:
0 for a valid preview or completed apply, 2 for validation/planning conflicts,
1 for a safely categorized exception. Errors and warnings identify line numbers,
not email addresses. Keep the source CSV unchanged.

The format is headerless `APELLIDO, Nombre ;email`, strict UTF-8 with optional
UTF-8 BOM. Surnames are trimmed, lowercased using es-AR, then only the first
character is uppercased. Given names and email case are preserved after trimming.
Exact normalized names and case-insensitive emails identify repeated rows; the
first email literal wins. Same-email/different-name rows block the entire import.
Same-name/different-email rows warn with all source lines and both remain eligible.
No email correction, name merging or silent omission is performed.

Existing emails are matched case-insensitively. Exactly one matching Auth and
User with the unambiguous normalized Docente role is skipped without changing any
field. Other roles, orphan credentials, ambiguous email matches or duplicate
normalized Docente roles block apply. A missing Docente role can be created in
the apply transaction, without permissions or responsibilities.

## Apply (Separate Authorization)

This command writes data. Do not run until the real preview has been reviewed.
Substitute all three values from that preview; no placeholder is a valid approval.

```powershell
dotnet run --project Tools/DocenteUserCsvImport -c Release -- --file ../docentes.csv --apply --expected-sha256 <sourceSha256> --expected-database <database> --expected-receipt <receipt>
```

The versioned receipt binds source bytes, safe target, current Users/Auths/Roles
scalar state, table counts and the complete preview summary. A stale receipt
rejects apply. Apply uses a serializable transaction with write-excluding locks
on Users/Auths/Roles and SHARE locks on other existing public tables for invariant
checks. It can briefly block local API writes. No pending table/navigation is
queried through the application model.

Each addition has a shared User/Auth GUID, the Docente role, Status=true and
MustChangePassword=false. Its credential is the exact case-sensitive substring
of the trimmed email before @, hashed exclusively through the existing configured
PasswordHasher. No plaintext credential is printed or persisted. Local parts
over BCrypt's 72-byte limit are rejected instead of truncated.

Before commit the tool verifies all table counts, the unchanged snapshot of
preexisting Users/Auths/Roles, and every inserted account's fields, role and
credential using PasswordHasher.Verify. Unexpected changes roll back. The CSV
is never written. Run a new preview after apply to confirm idempotent skips.

## Tests

```powershell
dotnet test Tests/CENS15_V2.Tests.csproj -c Release --filter FullyQualifiedName~DocenteUserCsvImportTests
```

Unit tests require no database. They cover parsing/BOM/strict decoding, literal
mapping and hashing, duplicate handling, loopback restrictions, role conflicts,
insert-only/idempotent planning and required apply confirmations. Real apply is
not executed by these tests.

## Link Existing Docentes

This independent mode creates no users and reads no CSV. Preview only:

```powershell
dotnet run --project Tools/DocenteUserCsvImport -c Release -- --link-docentes
```

It matches unlinked Docentes to active users with the Docente role and an Auth.
It first checks exact case-insensitive email plus matching full names, then
unique exact full names ignoring accents, case and repeated whitespace. It never
uses fuzzy/partial names or chooses between duplicate accounts. Existing links,
occupied users, identity conflicts and candidates claimed by multiple Docentes
are preserved or skipped with a reason.

After reviewing the preview, apply requires its database and receipt:

```powershell
dotnet run --project Tools/DocenteUserCsvImport -c Release -- --link-docentes --apply --expected-database <database> --expected-receipt <receipt>
```

Only `Docentes.UserId` is updated, conditionally on it still being null. The
serializable transaction locks Docentes/Users/Auths/Roles, checks all table
counts, and verifies all scalar columns of these four tables are unchanged
except the approved links. Credentials only participate in the private digest;
they are never emitted or changed. A fresh preview must propose zero further
links before commit. No migrations, seed, API startup or emails are performed.

See `RESULTADO_VINCULACION.md` for the actual local execution and pending cases.
