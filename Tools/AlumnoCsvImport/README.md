# Local Alumno CSV Import

Run from the API root. Requires .NET 8 and the existing local Development PostgreSQL configuration. No extra packages, migrations, API startup, seeding, checkpoint locks or backups are used. Only explicit `localhost`, `127.0.0.1` and `::1` hosts are accepted; remote or unavailable databases block execution without fallback. Configuration uses appsettings.json, appsettings.Development.json, then environment variables. Only `--environment Development` is supported (the default).

Preview (default, PostgreSQL read-only transaction):

```powershell
dotnet run --project Tools/AlumnoCsvImport/AlumnoCsvImport.csproj -c Release -- --file "C:\Development\CENS15\alumnos.csv"
```

Review the JSON summary. No source values or document numbers are printed. Source rows are physical starting line numbers, including the header. Equivalent duplicates are warnings; contradictory duplicates and invalid provided dates/genders or flags block all writes. UTF-8 with optional UTF-8 BOM is required. Empty values and legacy `NULL` markers remain blank. Names use the existing es-AR title casing. DNI comparison removes punctuation/whitespace, but the trimmed original string is stored. Missing/all-zero documents become SQL NULL, as do missing birth dates and genders. `hombre` maps to `Varon`; no gender is inferred from a name. No numeric DNI or phone conversion occurs. Literal quotes inside legacy quoted fields are escaped in memory and counted; the original CSV bytes are not modified.

Without a DNI, the importer uses normalized names, surname and nullable birth date as a conservative comparison key. It never combines different incomplete identities. Exact repeated mapped data is deduplicated; contradictory rows with the same comparison key block the import. Existing matches are skipped, never updated.

Document flags must be blank, 0 or 1. Blank flags or absent current catalog names omit document records and are counted in warnings. Existing catalog IDs only; no document types or images are created. Duplicate equivalence includes every mapped field and flag, but not ignored columns or DNI punctuation.

After reviewing and authorizing the preview, apply using the exact preview values:

```powershell
dotnet run --project Tools/AlumnoCsvImport/AlumnoCsvImport.csproj -c Release -- --file "C:\Development\CENS15\alumnos.csv" --apply --expected-sha256 SOURCE_SHA256 --expected-database DATABASE --expected-receipt RECEIPT
```

`--api-root PATH` optionally specifies the backend configuration directory (default: current directory). The source hash binds the exact bytes read into memory. The receipt binds the summary, sanitized target fingerprint, catalog IDs and current students; changed state requires another preview. There is no persistent receipt or source-data audit file. Exit codes: 0 success, 2 source validation blocked, 1 configuration/connection/confirmation/transaction failure. Error details are intentionally sanitized.

Apply re-reads all existing DNIs including soft-deleted rows under a serializable transaction and student-table SHARE ROW EXCLUSIVE locks. Academic/catalog SHARE locks stabilize verification. Existing DNIs are always skipped unchanged, never updated or restored. Only new students, birth/contact data and mapped document flags are inserted. Destination-generated IDs are used. No Inscripcion, Cursada or Calificacion is constructed or written; legacy id, inscripcion, curso_id, created_at and updated_at have no mapping. Before commit, the importer checks academic counts, no enrollments for inserted IDs and unchanged matched students/contact/birth/documents. Failure rolls back. Reruns skip existing DNIs. Apply output contains only counts, generated IDs and source row numbers.

The migration `20261103011616_AllowMissingAlumnoPersonalData` must be applied before importing incomplete profiles. It permits NULL only for the three student fields; the unique index still protects provided document numbers. Restart the API using the updated model before consulting incomplete profiles through an older running API process.

Focused tests:

```powershell
dotnet test Tests/CENS15_V2.Tests.csproj -c Release --filter FullyQualifiedName~AlumnoCsvImportTests
```

## Explicit 2026 Enrollment Mode

The default mode above still imports profiles only. The separate `--enroll` mode
is intentionally limited to courses 1F/1G/2F/3D/3E and year 2026, with source legacy
course IDs 28 for 1F, 25 for 1G, 32 for 2F, 33 for 3D and 34 for 3E. The 2F request uses
`alumnos_2H_2026.csv` as its source; the explicit destination takes precedence
over the filename. It does not enroll those students in 2H.
The legacy ID is validated but never used as the destination course ID. Exactly
one existing destination course with the requested year level and division must be resolved.
No courses, subjects, document types, users or grades are created.

Preview, using an explicit local Development configuration and READ ONLY transaction:

```powershell
dotnet run --project Tools/AlumnoCsvImport -c Release -- --enroll --file ../alumnos_1F_2026.csv --course 1F --year 2026
dotnet run --project Tools/AlumnoCsvImport -c Release -- --enroll --file ../alumnos_1G_2026.csv --course 1G --year 2026
dotnet run --project Tools/AlumnoCsvImport -c Release -- --enroll --file ../alumnos_2H_2026.csv --course 2F --year 2026
dotnet run --project Tools/AlumnoCsvImport -c Release -- --enroll --file ../alumnos_3D_2026.csv --course 3D --year 2026 --unknown-birth-date 9999-09-09
dotnet run --project Tools/AlumnoCsvImport -c Release -- --enroll --file ../alumnos_3E_2026.csv --course 3E --year 2026
```

This mode loads appsettings.json and appsettings.Development.json only, without
environment overrides or Production configuration. Loopback restrictions remain
mandatory. Preview binds the source, resolved course, year, subjects, document
catalog and fingerprints/counts of all public tables into the receipt.

After reviewing and authorizing the exact plan:

```powershell
dotnet run --project Tools/AlumnoCsvImport -c Release -- --enroll --file ../alumnos_1F_2026.csv --course 1F --year 2026 --apply --expected-sha256 SOURCE_SHA256 --expected-database DATABASE --expected-course-id COURSE_ID --expected-receipt RECEIPT
```

The single serializable transaction creates missing profiles, active enrollments
and one CursadaMateria per destination subject. Existing profiles are preserved;
deleted students, contradictory identities and inactive or incomplete existing
enrollments block apply. Exact active enrollments with complete subjects are
skipped. Contact field inversions supported by the source are repaired in memory
with warnings; suspicious emails are preserved, never guessed or corrected.

Invalid/future birth dates block the batch by default, including the legacy
sentinel `9999-09-09`. For 3D only, the explicitly approved
`--unknown-birth-date 9999-09-09` option blanks that exact sentinel in memory.
The affected physical source rows and option are included in the preview/receipt;
no other date is changed and the CSV bytes/hash remain intact. The user approved
this for Caren's row 13. NULL gender stays null and is never inferred from a name.

Before commit, the tool verifies expected count deltas, full unchanged snapshots
of preexisting rows, every new profile and enrollment, all subject assignments,
and an idempotent rerun plan. Any mismatch rolls back. The CSV is never written.
See the `RESULTADO_CARGA_*.md` files in this directory for actual execution results and warnings.

```powershell
dotnet test Tests/CENS15_V2.Tests.csproj -c Release --filter FullyQualifiedName~AlumnoCourseCsvImportTests
```
