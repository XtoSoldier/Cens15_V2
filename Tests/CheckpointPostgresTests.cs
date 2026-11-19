using System.Security.Claims;
using System.Text.RegularExpressions;
using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AuthDTOs;
using CENS15_V2.Models.DTOs.PreceptoresDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class SnapshotPostgresFactAttribute : FactAttribute
{
    public SnapshotPostgresFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CENS15_RUN_SNAPSHOT_POSTGRES_TESTS"), "true", StringComparison.Ordinal))
            Skip = "Opt-in only: set CENS15_RUN_SNAPSHOT_POSTGRES_TESTS=true for disposable local PostgreSQL tests.";
    }
}

public sealed class CheckpointPostgresTests
{
    private const string TemporaryParent = @"C:\Users\ferma\AppData\Local\Temp\opencode";
    private const string DatabasePrefix = "cens15_checkpoint_it_";
    private const string Passphrase = "disposable checkpoint integration passphrase";

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "CheckpointPostgresTests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class DisposableDatabase : IAsyncDisposable
    {
        private readonly string suffix = Guid.NewGuid().ToString("N");
        private string Name => DatabasePrefix + suffix;
        private string Root => Path.Combine(TemporaryParent, DatabasePrefix + suffix);
        private NpgsqlConnectionStringBuilder maintenance = null!;
        private bool created;
        public AppDbContext Db { get; private set; } = null!;
        public CheckpointStorage Storage { get; private set; } = null!;
        public SessionEpoch Epoch { get; private set; } = null!;
        public CheckpointGate Gate { get; } = new();
        public CheckpointModel Model { get; private set; } = null!;
        public CheckpointRecovery Recovery { get; private set; } = null!;
        public CheckpointService Service { get; private set; } = null!;
        public Guid AdminId { get; private set; }
        public ClaimsPrincipal Caller => new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, AdminId.ToString("D")), new Claim(ClaimTypes.Role, "Admin"),
            new Claim(SessionEpoch.Claim, Epoch.Current)], "IntegrationTest"));
        public string Upload => Path.Combine(Storage.Uploads, "alumno-documentos", "proof.bin");

        public AppDbContext NewContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        {
            ValidateGeneratedName();
            var isolated = new NpgsqlConnectionStringBuilder(Db.Database.GetConnectionString());
            if (!created || isolated.Database != Name || isolated.Host is not ("localhost" or "127.0.0.1"))
                throw new InvalidOperationException("Unsafe disposable context target refused.");
            return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(isolated.ConnectionString).AddInterceptors(interceptors).Options);
        }

        private void ValidateGeneratedName()
        {
            if (!Regex.IsMatch(Name, @"\Acens15_checkpoint_it_[a-f0-9]{32}\z") || Name != DatabasePrefix + suffix)
                throw new InvalidOperationException("Refusing an unvalidated disposable database name.");
        }

        public static async Task<DisposableDatabase> CreateAsync()
        {
            var instance = new DisposableDatabase();
            try { await instance.InitializeAsync(); return instance; }
            catch { await instance.DisposeAsync(); throw; }
        }

        private async Task InitializeAsync()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(TemporaryParent))
                throw new InvalidOperationException("Approved Windows temporary parent is unavailable; no database was touched.");
            var backend = new DirectoryInfo(AppContext.BaseDirectory);
            while (backend is not null && !File.Exists(Path.Combine(backend.FullName, "appsettings.Development.json"))) backend = backend.Parent;
            if (backend is null) throw new InvalidOperationException("Development connection configuration not found; no fallback is allowed.");
            // Never print the configured connection or preserve its database as an executable target.
            var configuration = new ConfigurationBuilder().SetBasePath(backend.FullName)
                .AddJsonFile("appsettings.Development.json", optional: false).Build();
            try
            {
                var configured = configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException();
                maintenance = new NpgsqlConnectionStringBuilder(configured);
            }
            catch { throw new InvalidOperationException("Development connection is missing or invalid; no fallback is allowed."); }
            if (maintenance.Host is not ("localhost" or "127.0.0.1"))
                throw new InvalidOperationException("Refusing non-local PostgreSQL configuration; only localhost/127.0.0.1 is allowed.");
            maintenance.Database = "postgres";
            maintenance.Pooling = false;
            maintenance.IncludeErrorDetail = false;
            maintenance.Timeout = 10;
            maintenance.CommandTimeout = 30;
            ValidateGeneratedName();
            try
            {
                await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("CREATE DATABASE \"" + Name + "\"", connection);
                await command.ExecuteNonQueryAsync();
                created = true;
            }
            catch (PostgresException ex)
            { throw new InvalidOperationException("Disposable local PostgreSQL CREATE DATABASE failed (SQLSTATE " + ex.SqlState + "). Verify CREATEDB privileges; application database was not accessed."); }
            catch
            { throw new InvalidOperationException("Cannot connect to local maintenance database postgres/create disposable database. Application database was not accessed."); }
            var isolated = new NpgsqlConnectionStringBuilder(maintenance.ConnectionString) { Database = Name };
            if (isolated.Database != Name || isolated.Database == "postgres") throw new InvalidOperationException("Unsafe integration target refused.");
            Directory.CreateDirectory(Root);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(isolated.ConnectionString).Options);
            await Db.Database.EnsureCreatedAsync(); // Only the newly generated database. Never Program/migrations/seeder.
            Storage = new CheckpointStorage(new ConfigurationBuilder().Build(), new TestEnvironment(Root));
            Epoch = new SessionEpoch(Storage);
            Model = new CheckpointModel(Db);
            Recovery = new CheckpointRecovery(Db, Storage, Epoch, Gate);
            await Recovery.InitializeAsync(prepareDatabase: true);
            Service = new CheckpointService(Db, Storage, Gate, Model, Recovery, Epoch, new CheckpointPreviews());
            await SeedAsync();
        }

        private async Task SeedAsync()
        {
            AdminId = Guid.NewGuid();
            var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
            role.Permissions.Add(new RolePermission { Role = role, Permission = new Permission { Id = Guid.NewGuid(), Name = "Legacy permission" } });
            role.Responsibilities.Add(new RoleResponsibility { Role = role, Responsibility = new Responsibility { Id = Guid.NewGuid(), Name = "Integration responsibility" } });
            var admin = new User { Id = AdminId, Role = role, FirstName = "Snapshot admin", LastName = "Integration", Status = true };
            admin.Auth = new Auth { Id = AdminId, User = admin, Email = "snapshot@example.invalid", PasswordHash = BCrypt.Net.BCrypt.HashPassword("disposable password", 4) };
            Db.Add(admin);
            Db.Add(new Token { Id = AdminId, Auth = admin.Auth, AccessToken = "historical-session" });
            Db.Add(new PasswordResetCode { Auth = admin.Auth, CodeHash = "historical-reset", ExpiresAt = DateTime.UtcNow.AddMinutes(10) });
            var anexo = new Anexo { Nombre = "Snapshot annex" };
            var curso = new Curso { CursoNombre = "Third year", Division = "A", Anexo = anexo,
                Orientacion = new Orientacion { Nombre = "Snapshot orientation", NombreCorto = "SI" } };
            var materia = new Materia { Nombre = "Snapshot subject", Curso = curso, CargaHoraria = 4 };
            var horario = new HorarioAnexo { Anexo = anexo, DiaSemana = DayOfWeek.Monday, Orden = 1,
                HoraInicio = new TimeOnly(18, 0), HoraFin = new TimeOnly(18, 40), Tipo = TipoBloqueHorario.Clase };
            Db.Add(new HorarioMateria { Curso = curso, Materia = materia, HorarioAnexo = horario });
            Db.Add(new MateriaDocente { Materia = materia, Docente = new Docente { Nombres = "Teacher", Apellidos = "Integration", User = admin }, Rol = "Titular", Activo = true });
            var alumno = new Alumno { Nombres = "Deleted student", Apellidos = "Integration", NumeroDocumento = "31415926",
                Eliminado = true, FechaEliminacion = DateTime.UtcNow, FechaNacimiento = new DateOnly(1990, 2, 3),
                DatosNacimiento = new AlumnoNacimiento { Localidad = "Town", Provincia = "Province", Pais = "Argentina" },
                Contacto = new AlumnoContacto { Email = "student@example.invalid", TelefonoAlumno = "123456" } };
            Db.Add(new AlumnoDocumento { Alumno = alumno, TipoDocumentoAlumno = new TipoDocumentoAlumno { Nombre = "Integration document" },
                Presentado = true, ImagenUrl = "/uploads/alumno-documentos/proof.bin",
                ImagenesUrl = ["/uploads/alumno-documentos/proof.bin", "/uploads/alumno-documentos/second.bin"] });
            var inscripcion = new Inscripcion { Alumno = alumno, Curso = curso, Anio = 2026, FechaInscripcion = DateTime.UtcNow };
            Db.Add(new Calificacion { CursadaMateria = new CursadaMateria { Inscripcion = inscripcion, Materia = materia, MateriaNombre = materia.Nombre },
                MateriaNombre = materia.Nombre, C1Nota1 = 8.5m, CalificacionFinal = 9m });
            Db.Add(new CertificadoTemplate { Nombre = "Snapshot certificate", ContenidoHtml = "<p>snapshot</p>", CreatedAt = DateTime.UtcNow });
            Db.Add(new LoginActivity { Email = admin.Auth.Email, User = admin, Success = true, CreatedAt = DateTime.UtcNow });
            await Db.SaveChangesAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(Upload)!);
            await File.WriteAllBytesAsync(Upload, [0, 1, 2, 255]);
            await File.WriteAllBytesAsync(Path.Combine(Path.GetDirectoryName(Upload)!, "second.bin"), [8, 9]);
        }

        public async Task SetLiveStateAsync(string name = "Live admin")
        {
            Db.ChangeTracker.Clear();
            (await Db.Users.SingleAsync()).FirstName = name;
            await Db.SaveChangesAsync();
            await File.WriteAllBytesAsync(Upload, [9, 9, 9, 9]);
        }

        public async Task AssertLiveAsync(string expectedEpoch, string name = "Live admin")
        {
            Db.ChangeTracker.Clear();
            Assert.Equal(name, (await Db.Users.SingleAsync()).FirstName);
            Assert.Equal(new byte[] { 9, 9, 9, 9 }, await File.ReadAllBytesAsync(Upload));
            Assert.Equal(expectedEpoch, Epoch.Current);
            Assert.True(Gate.Healthy);
            Assert.False(File.Exists(Path.Combine(Storage.Root, "restore.journal")));
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Db is not null) await Db.DisposeAsync();
                Storage?.Dispose();
            }
            finally
            {
                try
                {
                    if (created)
                    {
                        ValidateGeneratedName();
                        if (maintenance.Host is not ("localhost" or "127.0.0.1") || maintenance.Database != "postgres")
                            throw new InvalidOperationException("Unsafe maintenance target refused during cleanup.");
                        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
                        await connection.OpenAsync();
                        await using var command = new NpgsqlCommand("DROP DATABASE \"" + Name + "\" WITH (FORCE)", connection);
                        await command.ExecuteNonQueryAsync();
                        created = false;
                    }
                }
                finally
                {
                    if (Directory.Exists(Root)) Directory.Delete(Root, true);
                }
            }
        }
    }

    [SnapshotPostgresFact]
    public async Task CalificacionOrigenRoundTripsOnlyInDisposablePostgresAndCleansUp()
    {
        string databaseName;
        NpgsqlConnectionStringBuilder maintenance;
        await using (var f = await DisposableDatabase.CreateAsync())
        {
            maintenance = new NpgsqlConnectionStringBuilder(f.Db.Database.GetConnectionString());
            databaseName = maintenance.Database!;
            var mapper = new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper();
            var courseId = await f.Db.Cursos.Select(c => c.Id).SingleAsync();
            var subjectId = await f.Db.Materias.Select(m => m.Id).SingleAsync();
            var attendance = new CursadaMateria { MateriaId = subjectId, MateriaNombre = "Origen descartable",
                Inscripcion = new Inscripcion { CursoId = courseId, Anio = 2026, FechaInscripcion = DateTime.UtcNow,
                    Alumno = new Alumno { Nombres = "Origen", Apellidos = "Descartable" } } };
            f.Db.Add(attendance);
            await f.Db.SaveChangesAsync();
            var service = new CalificacionService(f.Db, mapper);
            var created = await service.CreateAsync(new CENS15_V2.Models.DTOs.CalificacionesDTOs.CreateCalificacionRequest
            {
                CursadaMateriaId = attendance.Id, Estado = EstadoMateria.AprobadoPorEquivalencia,
                DisposicionEquivalencia = "  Texto libre <script> & ' sin formato  ",
                C1Promedio = 5, C2Promedio = 8.25m, C2Condicion = CondicionNota.SinCalificar,
                C1Nota1 = 1.1m, C1Nota2 = 2.2m, C1Nota3 = 3.3m,
                C2Nota1 = 4.4m, C2Nota2 = 5.5m, C2Nota3 = 6.6m,
                RecuperacionDiciembre = 7.25m, DiciembreCondicion = CondicionNota.Ausente
            });
            f.Db.ChangeTracker.Clear();
            await using var isolated = f.NewContext();
            var reader = new CalificacionService(isolated, mapper);
            var loaded = (await reader.GetByCursadaMateriaIdAsync(attendance.Id))!;
            Assert.Equal(EstadoMateria.AprobadoPorEquivalencia, loaded.Estado);
            Assert.Equal("Texto libre <script> & ' sin formato", loaded.DisposicionEquivalencia);
            Assert.False(loaded.DesdePase);
            Assert.Null(loaded.CalificacionFinal);
            Assert.Null(loaded.PromedioAnual);
            Assert.Equal(8.25m, loaded.C2Promedio);
            Assert.Equal(7.25m, loaded.RecuperacionDiciembre);
            Assert.Equal(6.6m, loaded.C2Nota3);
            await using (var command = isolated.Database.GetDbConnection().CreateCommand())
            {
                await isolated.Database.OpenConnectionAsync();
                command.CommandText = "SELECT \"Estado\" FROM \"Calificaciones\" WHERE \"Id\" = @id";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "id";
                parameter.Value = created.Id;
                command.Parameters.Add(parameter);
                Assert.Equal("AprobadoPorEquivalencia", await command.ExecuteScalarAsync());
            }
            var update = new CENS15_V2.Models.DTOs.CalificacionesDTOs.UpdateCalificacionRequest
            {
                CursadaMateriaId = attendance.Id, Estado = EstadoMateria.EnCurso, DesdePase = true,
                C1Promedio = 5, C2Promedio = loaded.C2Promedio, C2Condicion = CondicionNota.SinCalificar,
                RecuperacionDiciembre = loaded.RecuperacionDiciembre, DiciembreCondicion = CondicionNota.Ausente,
                C1Nota1 = loaded.C1Nota1, C1Nota2 = loaded.C1Nota2, C1Nota3 = loaded.C1Nota3,
                C2Nota1 = loaded.C2Nota1, C2Nota2 = loaded.C2Nota2, C2Nota3 = loaded.C2Nota3
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(created.Id, update));
            await f.Db.SaveChangesAsync();
            Assert.Equal(EstadoMateria.AprobadoPorEquivalencia, (await reader.GetByIdAsync(created.Id))!.Estado);
            update.RecuperacionExcepcional = EstadoMateria.RecuperaAmbos;
            Assert.True(await service.UpdateAsync(created.Id, update));
            loaded = (await reader.GetByIdAsync(created.Id))!;
            Assert.Equal(EstadoMateria.RecuperaAmbos, loaded.Estado);
            Assert.True(loaded.DesdePase);
            Assert.Null(loaded.DisposicionEquivalencia);
            Assert.Null(loaded.C2Promedio);
            Assert.Equal(0m, loaded.RecuperacionDiciembre);
            Assert.Equal(5m, loaded.PromedioAnual);
            Assert.Null(loaded.CalificacionFinal);
        }
        maintenance.Database = "postgres";
        maintenance.Pooling = false;
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();
        await using var check = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", connection);
        check.Parameters.AddWithValue("name", databaseName);
        Assert.Equal(0L, await check.ExecuteScalarAsync());
    }

    private sealed class PauseAfterDeletionLock : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public TaskCompletionSource Locked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool locked;
        public override ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData eventData, System.Data.Common.DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) locked = true;
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (locked)
            {
                locked = false;
                Locked.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }

    [SnapshotPostgresFact]
    public async Task PermanentDeletionUsesDisposablePostgresAndNeverCascadesConcurrentChildren()
    {
        string databaseName;
        NpgsqlConnectionStringBuilder maintenance;
        await using (var f = await DisposableDatabase.CreateAsync())
        {
            maintenance = new NpgsqlConnectionStringBuilder(f.Db.Database.GetConnectionString());
            databaseName = maintenance.Database!;
            var mapper = new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper();
            var service = new AlumnoService(f.Db, mapper);
            var original = await f.Db.Alumnos.IgnoreQueryFilters().SingleAsync();
            var info = await service.GetPermanentDeletionInfoAsync(original.Id);
            Assert.Equal(new[] { 1, 1, 1, 1, 1, 1 }, info!.Dependencias.Select(d => d.Cantidad));
            await Assert.ThrowsAsync<DeletionConflictException>(() => service.DeletePermanentlyAsync(original.Id));
            await Assert.ThrowsAsync<DeletionConflictException>(() => new InscripcionService(f.Db, mapper)
                .DeleteAsync(f.Db.Inscripciones.IgnoreQueryFilters().Single().Id));
            Assert.All(f.Db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
            Assert.True(File.Exists(f.Upload));

            // The inscription deletion path must also lock its own row against new cursadas.
            var emptyInscripcion = new Inscripcion { Alumno = new Alumno { Nombres = "Empty inscription", Eliminado = true },
                CursoId = await f.Db.Cursos.Select(c => c.Id).SingleAsync(), Anio = 2026, FechaInscripcion = DateTime.UtcNow };
            f.Db.Add(emptyInscripcion);
            await f.Db.SaveChangesAsync();
            var inscriptionPause = new PauseAfterDeletionLock();
            await using (var deleting = f.NewContext(inscriptionPause))
            await using (var inserting = f.NewContext())
            {
                await inserting.Database.OpenConnectionAsync();
                var pid = ((NpgsqlConnection)inserting.Database.GetDbConnection()).ProcessID;
                inserting.Add(new CursadaMateria { InscripcionId = emptyInscripcion.Id,
                    MateriaId = await f.Db.Materias.Select(m => m.Id).SingleAsync() });
                var deletion = new InscripcionService(deleting, mapper).DeleteAsync(emptyInscripcion.Id);
                Task<int>? insertion = null;
                try
                {
                    await inscriptionPause.Locked.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    insertion = inserting.SaveChangesAsync();
                    await using var observer = f.NewContext();
                    var waiting = false;
                    for (var attempt = 0; attempt < 100 && !waiting; attempt++)
                    {
                        waiting = await observer.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock'").SingleAsync() == 1;
                        if (!waiting) await Task.Delay(50);
                    }
                    Assert.True(waiting, "Cursada FK insert must block on the inscription FOR UPDATE lock.");
                }
                finally { inscriptionPause.Resume.TrySetResult(); }
                Assert.True(await deletion);
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => insertion!);
                Assert.Equal("23503", Assert.IsType<PostgresException>(error.InnerException).SqlState);
                await using var check = f.NewContext();
                Assert.False(await check.Inscripciones.IgnoreQueryFilters().AnyAsync(i => i.Id == emptyInscripcion.Id));
                Assert.True(await new AlumnoService(check, mapper).DeletePermanentlyAsync(emptyInscripcion.AlumnoId));
            }

            // Every direct FK insertion must wait behind the service's parent-row lock.
            foreach (var child in new[] { "AlumnoContactos", "AlumnoNacimientos", "AlumnoDocumentos", "Inscripciones" })
            {
                var alumno = new Alumno { Nombres = "Disposable", Eliminado = true };
                f.Db.Add(alumno);
                await f.Db.SaveChangesAsync();
                var pause = new PauseAfterDeletionLock();
                await using var deleting = f.NewContext(pause);
                await using var inserting = f.NewContext();
                await inserting.Database.OpenConnectionAsync();
                var pid = ((NpgsqlConnection)inserting.Database.GetDbConnection()).ProcessID;
                switch (child)
                {
                    case "AlumnoContactos": inserting.Add(new AlumnoContacto { AlumnoId = alumno.Id }); break;
                    case "AlumnoNacimientos": inserting.Add(new AlumnoNacimiento { AlumnoId = alumno.Id }); break;
                    case "AlumnoDocumentos": inserting.Add(new AlumnoDocumento { AlumnoId = alumno.Id,
                        TipoDocumentoAlumnoId = await f.Db.TiposDocumentoAlumno.Select(t => t.Id).SingleAsync() }); break;
                    case "Inscripciones": inserting.Add(new Inscripcion { AlumnoId = alumno.Id,
                        CursoId = await f.Db.Cursos.Select(c => c.Id).SingleAsync(), Anio = 2026, FechaInscripcion = DateTime.UtcNow }); break;
                }
                var deletion = new AlumnoService(deleting, mapper).DeletePermanentlyAsync(alumno.Id);
                Task<int>? insertion = null;
                try
                {
                    await pause.Locked.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    insertion = inserting.SaveChangesAsync();
                    var waiting = false;
                    await using var observer = f.NewContext();
                    for (var attempt = 0; attempt < 100 && !waiting; attempt++)
                    {
                        waiting = await observer.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock'").SingleAsync() == 1;
                        if (!waiting) await Task.Delay(50);
                    }
                    Assert.True(waiting, "Concurrent child FK insert must block on the parent FOR UPDATE lock.");
                }
                finally { pause.Resume.TrySetResult(); }
                Assert.True(await deletion);
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => insertion!);
                Assert.Equal("23503", Assert.IsType<PostgresException>(error.InnerException).SqlState);
                await using var check = f.NewContext();
                Assert.False(await check.Alumnos.IgnoreQueryFilters().AnyAsync(a => a.Id == alumno.Id));
            }

            // The reverse race: insert holds KEY SHARE before deletion takes its snapshot/lock.
            // A later child commit must produce 409, never cascade that newly committed child.
            foreach (var child in new[] { "AlumnoContactos", "AlumnoNacimientos", "AlumnoDocumentos", "Inscripciones" })
            {
                var alumno = new Alumno { Nombres = "Keep concurrent child" };
                f.Db.Add(alumno);
                await f.Db.SaveChangesAsync();
                await using var inserting = f.NewContext();
                await using var insertTransaction = await inserting.Database.BeginTransactionAsync();
                switch (child)
                {
                    case "AlumnoContactos": inserting.Add(new AlumnoContacto { AlumnoId = alumno.Id }); break;
                    case "AlumnoNacimientos": inserting.Add(new AlumnoNacimiento { AlumnoId = alumno.Id }); break;
                    case "AlumnoDocumentos": inserting.Add(new AlumnoDocumento { AlumnoId = alumno.Id,
                        TipoDocumentoAlumnoId = await f.Db.TiposDocumentoAlumno.Select(t => t.Id).SingleAsync() }); break;
                    case "Inscripciones": inserting.Add(new Inscripcion { AlumnoId = alumno.Id,
                        CursoId = await f.Db.Cursos.Select(c => c.Id).SingleAsync(), Anio = 2026, FechaInscripcion = DateTime.UtcNow }); break;
                }
                await inserting.SaveChangesAsync();
                await using var deleting = f.NewContext();
                await deleting.Database.OpenConnectionAsync();
                var pid = ((NpgsqlConnection)deleting.Database.GetDbConnection()).ProcessID;
                var deletion = new AlumnoService(deleting, mapper).DeletePermanentlyAsync(alumno.Id);
                try
                {
                    await using var observer = f.NewContext();
                    var waiting = false;
                    for (var attempt = 0; attempt < 100 && !waiting; attempt++)
                    {
                        waiting = await observer.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock'").SingleAsync() == 1;
                        if (!waiting) await Task.Delay(50);
                    }
                    Assert.True(waiting, "Deletion must wait for the in-flight child insert.");
                }
                finally { await insertTransaction.CommitAsync(); }
                await Assert.ThrowsAsync<DeletionConflictException>(() => deletion);
                await using var check = f.NewContext();
                var after = await new AlumnoService(check, mapper).GetPermanentDeletionInfoAsync(alumno.Id);
                Assert.False(after!.PuedeEliminar);
                Assert.Equal(1, after.Dependencias.Single(d => d.Entidad == child).Cantidad);
                Assert.All(deleting.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
            }

            // A child committed before validation blocks deletion and remains unchanged.
            await using (var adding = f.NewContext())
            {
                var alumno = new Alumno { Nombres = "Keep", Contacto = new AlumnoContacto() };
                adding.Add(alumno);
                await adding.SaveChangesAsync();
                await using var deleting = f.NewContext();
                await Assert.ThrowsAsync<DeletionConflictException>(() => new AlumnoService(deleting, mapper).DeletePermanentlyAsync(alumno.Id));
                Assert.True(await adding.AlumnoContactos.IgnoreQueryFilters().AnyAsync(c => c.AlumnoId == alumno.Id));
            }
            Assert.False(await service.DeletePermanentlyAsync(int.MaxValue));
            Assert.Single(await f.Db.Calificaciones.IgnoreQueryFilters().ToListAsync());
            Assert.True(File.Exists(f.Upload));
        }
        // Confirm that fixture cleanup actually removed the generated database.
        maintenance.Database = "postgres";
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", connection);
        command.Parameters.AddWithValue("name", databaseName);
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    private sealed class OtpEmail : IEmailService
    {
        public string Code { get; private set; } = string.Empty;
        public Task SendInitialAccessAsync(Auth auth, string temporaryPassword) => throw new InvalidOperationException("OTP email required.");
        public Task SendPasswordResetCodeAsync(Auth auth, string verificationCode)
        {
            Code = verificationCode;
            return Task.CompletedTask;
        }
    }

    private sealed class OtpTokens : ITokenService
    {
        public AuthResponse CreateToken(Auth auth) => throw new InvalidOperationException("OTP requests must not issue tokens.");
    }

    private static AuthService OtpService(AppDbContext db, OtpEmail email) => new(db, new OtpTokens(),
        new PasswordHasher(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Security:BCryptWorkFactor"] = "4" }).Build()), email);

    [SnapshotPostgresFact]
    public async Task PreceptorAssignmentsEnforceRelationalIntegrityAndRollback()
    {
        string databaseName;
        NpgsqlConnectionStringBuilder maintenance;
        await using (var f = await DisposableDatabase.CreateAsync())
        {
            // Derive cleanup verification only from the fixture's validated disposable target.
            await using (var target = f.NewContext())
            {
                maintenance = new NpgsqlConnectionStringBuilder(target.Database.GetConnectionString());
                databaseName = maintenance.Database!;
            }
            var mapper = new MapperConfiguration(config => config.AddProfile<AutoMapperProfile>()).CreateMapper();
            var userId = Guid.NewGuid();
            int firstCourseId;
            int secondCourseId;
            int freeCourseId;
            await using (var setup = f.NewContext())
            {
                var existing = await setup.Cursos.SingleAsync();
                firstCourseId = existing.Id;
                var second = new Curso { CursoNombre = "Second year", Division = "B",
                    AnexoId = existing.AnexoId, OrientacionId = existing.OrientacionId };
                var free = new Curso { CursoNombre = "First year", Division = "C",
                    AnexoId = existing.AnexoId, OrientacionId = existing.OrientacionId };
                setup.Cursos.AddRange(second, free);
                setup.Users.Add(new User { Id = userId, FirstName = "Preceptor", LastName = "Integration",
                    Status = true, Role = new Role { Id = Guid.NewGuid(), Name = "Preceptor" } });
                await setup.SaveChangesAsync();
                secondCourseId = second.Id;
                freeCourseId = free.Id;
            }

            var request = new GuardarPreceptorRequest { Nombres = "Preceptor", Apellidos = "Integration",
                UserId = userId, CursoIds = [firstCourseId, secondCourseId] };
            int preceptorId;
            await using (var create = f.NewContext())
            {
                var item = await new PreceptorService(create, mapper).CreateAsync(request);
                preceptorId = item.Id;
                Assert.Equal(request.CursoIds.Order(), item.Cursos.Select(c => c.Id).Order());
                Assert.Equal(userId, item.UserId);
            }
            await using (var occupied = f.NewContext())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => new PreceptorService(occupied, mapper)
                    .CreateAsync(new GuardarPreceptorRequest { Nombres = "Other", Apellidos = "Preceptor",
                        CursoIds = [secondCourseId] }));
            }
            await using (var duplicate = f.NewContext())
            {
                duplicate.Preceptores.Add(new Preceptor { Nombres = "Duplicate", Apellidos = "User", UserId = userId });
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
                var postgres = Assert.IsType<PostgresException>(error.InnerException);
                Assert.Equal("23505", postgres.SqlState);
                Assert.Equal("IX_Preceptores_UserId", postgres.ConstraintName);
            }
            await using (var check = f.NewContext())
            {
                Assert.Equal(1, await check.Preceptores.CountAsync());
                Assert.Equal(3, await check.Cursos.CountAsync());
                Assert.Equal(2, await check.Users.CountAsync());
                Assert.Equal(2, await check.Cursos.CountAsync(c => c.PreceptorId == preceptorId));
                Assert.Null((await check.Cursos.SingleAsync(c => c.Id == freeCourseId)).PreceptorId);
            }

            await using (var invalid = f.NewContext())
            {
                Assert.False(await invalid.Preceptores.AnyAsync(p => p.Id == int.MaxValue));
                var pending = new Preceptor { Nombres = "Must rollback", Apellidos = "Integration" };
                invalid.Preceptores.Add(pending);
                (await invalid.Cursos.SingleAsync(c => c.Id == freeCourseId)).Preceptor = pending;
                (await invalid.Cursos.SingleAsync(c => c.Id == secondCourseId)).PreceptorId = int.MaxValue;
                // One SaveChanges inserts a parent and updates two courses; the invalid FK must undo all writes.
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
                var postgres = Assert.IsType<PostgresException>(error.InnerException);
                Assert.Equal("23503", postgres.SqlState);
                Assert.Equal("FK_Cursos_Preceptores_PreceptorId", postgres.ConstraintName);
            }
            await using (var check = f.NewContext())
            {
                Assert.Equal(preceptorId, (await check.Preceptores.SingleAsync()).Id);
                Assert.Equal(3, await check.Cursos.CountAsync());
                Assert.Equal(2, await check.Users.CountAsync());
                Assert.Equal(2, await check.Cursos.CountAsync(c => c.PreceptorId == preceptorId));
                Assert.Null((await check.Cursos.SingleAsync(c => c.Id == freeCourseId)).PreceptorId);
            }

            await using (var delete = f.NewContext())
            {
                // Do not load courses: SET NULL must be performed by PostgreSQL, not relationship fixup.
                delete.Preceptores.Remove(await delete.Preceptores.SingleAsync());
                await delete.SaveChangesAsync();
            }
            await using (var check = f.NewContext())
            {
                Assert.Empty(await check.Preceptores.ToListAsync());
                Assert.Equal(3, await check.Cursos.CountAsync());
                Assert.All(await check.Cursos.ToListAsync(), c => Assert.Null(c.PreceptorId));
                Assert.True(await check.Users.AnyAsync(u => u.Id == userId));
            }
            await using (var recreate = f.NewContext())
                preceptorId = (await new PreceptorService(recreate, mapper).CreateAsync(request)).Id;
            await using (var delete = f.NewContext())
            {
                // Likewise leave the preceptor unloaded to exercise ON DELETE SET NULL on UserId.
                delete.Users.Remove(await delete.Users.SingleAsync(u => u.Id == userId));
                await delete.SaveChangesAsync();
            }
            await using (var check = f.NewContext())
            {
                var item = await check.Preceptores.SingleAsync();
                Assert.Equal(preceptorId, item.Id);
                Assert.Null(item.UserId);
                Assert.False(await check.Users.AnyAsync(u => u.Id == userId));
                Assert.Equal(3, await check.Cursos.CountAsync());
                Assert.Equal(2, await check.Cursos.CountAsync(c => c.PreceptorId == preceptorId));
            }
        }

        maintenance.Database = "postgres";
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", connection);
        command.Parameters.AddWithValue("name", databaseName);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        Assert.False(Directory.Exists(Path.Combine(TemporaryParent, databaseName)));
    }

    [SnapshotPostgresFact]
    public async Task OtpInitialAccessConcurrentRedeemSucceedsExactlyOnceAndRejectsReplay()
    {
        await using var f = await DisposableDatabase.CreateAsync();
        var email = new OtpEmail();
        var original = await f.Db.Auths.SingleAsync();
        original.MustChangePassword = true;
        await f.Db.SaveChangesAsync();
        var originalHash = original.PasswordHash;
        await using (var issue = f.NewContext())
            await OtpService(issue, email).RequestInitialAccessAsync(new InitialAccessRequest { Email = original.Email });
        Assert.Matches("^[0-9]{6}$", email.Code);
        await using (var check = f.NewContext())
        {
            var unchanged = await check.Auths.SingleAsync();
            Assert.Equal(originalHash, unchanged.PasswordHash);
            Assert.True(unchanged.MustChangePassword);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Redeem(string password)
        {
            await using var db = f.NewContext();
            await start.Task;
            return await Record.ExceptionAsync(() => OtpService(db, email).ResetPasswordAsync(new ResetPasswordRequest
                { Email = original.Email, Code = email.Code, NewPassword = password }));
        }
        var first = Redeem("first-new-password");
        var second = Redeem("second-new-password");
        start.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results, result => result == null);
        Assert.IsType<InvalidOperationException>(Assert.Single(results, result => result != null));
        await using var verify = f.NewContext();
        var changed = await verify.Auths.SingleAsync();
        Assert.False(changed.MustChangePassword);
        Assert.True(BCrypt.Net.BCrypt.Verify(results[0] == null ? "first-new-password" : "second-new-password", changed.PasswordHash));
        Assert.All(await verify.PasswordResetCodes.ToListAsync(), code => Assert.NotNull(code.UsedAt));
        await Assert.ThrowsAsync<InvalidOperationException>(() => OtpService(verify, email).ResetPasswordAsync(
            new ResetPasswordRequest { Email = original.Email, Code = email.Code, NewPassword = "replay-password" }));
    }

    [SnapshotPostgresFact]
    public async Task OtpWrongAttemptsCommitAcrossContextsAndResendReplacesBlockedCode()
    {
        await using var f = await DisposableDatabase.CreateAsync();
        var email = new OtpEmail();
        var original = await f.Db.Auths.SingleAsync();
        await using (var issue = f.NewContext())
            await OtpService(issue, email).RequestPasswordResetAsync(new PasswordResetRequest { Email = original.Email });
        var oldCode = email.Code;
        var codeId = await f.Db.PasswordResetCodes.MaxAsync(code => code.Id);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await using (var request = f.NewContext())
            {
                var service = OtpService(request, email);
                await Assert.ThrowsAsync<InvalidOperationException>(() => attempt % 2 == 0
                    ? service.ResetPasswordAsync(new ResetPasswordRequest { Email = original.Email, Code = "000000", NewPassword = "new-password" })
                    : service.ValidatePasswordResetCodeAsync(new ValidatePasswordResetCodeRequest { Email = original.Email, Code = "000000" }));
            }
            await using var check = f.NewContext();
            Assert.Equal(attempt, (await check.PasswordResetCodes.SingleAsync(code => code.Id == codeId)).Attempts);
            Assert.Equal(original.PasswordHash, (await check.Auths.SingleAsync()).PasswordHash);
        }
        await using (var blocked = f.NewContext())
        {
            var service = OtpService(blocked, email);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidatePasswordResetCodeAsync(
                new ValidatePasswordResetCodeRequest { Email = original.Email, Code = oldCode }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResetPasswordAsync(
                new ResetPasswordRequest { Email = original.Email, Code = oldCode, NewPassword = "new-password" }));
        }

        // A random resend can repeat the same six digits; obtain a distinct value before comparing.
        for (var send = 0; send < 3 && email.Code == oldCode; send++)
        {
            await using var resend = f.NewContext();
            await OtpService(resend, email).RequestPasswordResetAsync(new PasswordResetRequest { Email = original.Email });
        }
        Assert.NotEqual(oldCode, email.Code);
        await using (var stale = f.NewContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() => OtpService(stale, email).ValidatePasswordResetCodeAsync(
                new ValidatePasswordResetCodeRequest { Email = original.Email, Code = oldCode }));
        await using (var valid = f.NewContext())
            await OtpService(valid, email).ValidatePasswordResetCodeAsync(
                new ValidatePasswordResetCodeRequest { Email = original.Email, Code = email.Code });
        await using (var reset = f.NewContext())
            await OtpService(reset, email).ResetPasswordAsync(
                new ResetPasswordRequest { Email = original.Email, Code = email.Code, NewPassword = "resend-password" });
        await using var final = f.NewContext();
        Assert.True(BCrypt.Net.BCrypt.Verify("resend-password", (await final.Auths.SingleAsync()).PasswordHash));
        Assert.All(await final.PasswordResetCodes.ToListAsync(), code => Assert.NotNull(code.UsedAt));
        Assert.Equal(5, (await final.PasswordResetCodes.SingleAsync(code => code.Id == codeId)).Attempts);
    }

    [SnapshotPostgresFact]
    public async Task FullRelationalRoundtripPreservesScalarsFilesIdsAndCreatesPreBackupWithoutReactivatingCodes()
    {
        await using var f = await DisposableDatabase.CreateAsync();
        var point = await f.Service.CreateAsync(new("Relational snapshot", null), f.Caller, default);
        var original = f.Storage.Load(point.Id);
        Assert.All(original.Entidades, table => Assert.NotEmpty(table.Value));
        var archive = await f.Service.ExportAsync(point.Id, Passphrase, f.Caller, default);
        await f.SetLiveStateAsync();
        var epoch = f.Epoch.Current;
        var imported = await f.Service.ImportAsync(archive, Passphrase, f.Caller, default);
        await f.AssertLiveAsync(epoch); // Registering the portable archive must not restore its rows/files.
        var preview = await f.Service.PreviewAsync(imported.Id, f.Caller, default);
        Assert.True(preview.PuedeRestaurar, string.Join("; ", preview.Errores));
        Assert.Equal(0, preview.Tablas.Single(t => t.Entidad == typeof(Token).FullName).Snapshot);
        Assert.Equal(0, preview.Tablas.Single(t => t.Entidad == typeof(PasswordResetCode).FullName).Snapshot);
        var result = await f.Service.RestoreAsync(imported.Id, new(preview.Huella, "RESTAURAR TODO"), f.Caller, default);
        Assert.True(result.Restaurado);
        Assert.True(result.RequiereReingreso);
        Assert.NotNull(result.RespaldoPrevioId);
        var backup = f.Storage.Load(result.RespaldoPrevioId!.Value);
        Assert.True(backup.Automatico);
        Assert.Equal("Live admin", backup.Entidades[typeof(User).FullName!][0]["FirstName"].GetString());
        Assert.Equal(new byte[] { 9, 9, 9, 9 }, backup.Archivos["alumno-documentos/proof.bin"]);
        Assert.NotEqual(epoch, f.Epoch.Current);
        Assert.Empty(await f.Db.Tokens.ToListAsync());
        Assert.Empty(await f.Db.PasswordResetCodes.ToListAsync());
        var restored = await f.Model.CaptureAsync(f.Storage, "assert", null, f.AdminId.ToString(), false, default);
        foreach (var table in original.Entidades.Where(t => t.Key != typeof(Token).FullName && t.Key != typeof(PasswordResetCode).FullName))
        {
            var restoredRows = restored.Entidades[table.Key];
            Assert.Equal(table.Value.Count, restoredRows.Count);
            for (var i = 0; i < table.Value.Count; i++)
            {
                Assert.Equal(table.Value[i].Keys.Order(StringComparer.Ordinal), restoredRows[i].Keys.Order(StringComparer.Ordinal));
                foreach (var property in table.Value[i])
                    Assert.Equal(property.Value.GetRawText(), restoredRows[i][property.Key].GetRawText());
            }
        }
        Assert.Equal(original.HuellaEsquema, restored.HuellaEsquema);
        Assert.Equal(original.Archivos.Keys, restored.Archivos.Keys);
        foreach (var file in original.Archivos) Assert.Equal(file.Value, restored.Archivos[file.Key]);
        Assert.Equal(2, (await f.Db.AlumnoDocumentos.IgnoreQueryFilters().SingleAsync()).ImagenesUrl.Count);
        Assert.Empty(await f.Db.Alumnos.ToListAsync());
        Assert.Single(await f.Db.Alumnos.IgnoreQueryFilters().ToListAsync());
        var maxStudent = await f.Db.Alumnos.IgnoreQueryFilters().MaxAsync(a => a.Id);
        var nextStudent = new Alumno { Nombres = "New", Apellidos = "Student", NumeroDocumento = "27182818" };
        f.Db.Add(nextStudent); await f.Db.SaveChangesAsync();
        Assert.Equal(maxStudent + 1, nextStudent.Id);
        var nextReset = new PasswordResetCode { AuthId = f.AdminId, CodeHash = "new-reset", ExpiresAt = DateTime.UtcNow.AddMinutes(5) };
        f.Db.Add(nextReset); await f.Db.SaveChangesAsync();
        Assert.True(nextReset.Id > 0);
        Assert.True(f.Gate.Healthy);
        Assert.True(await f.Recovery.HasRestoredAsync());
    }

    [SnapshotPostgresFact]
    public async Task StaleDataAndSameLengthFileChangesRequireFreshPreview()
    {
        await using var f = await DisposableDatabase.CreateAsync();
        var point = await f.Service.CreateAsync(new("Stale preview", null), f.Caller, default);
        var epoch = f.Epoch.Current;
        var preview = await f.Service.PreviewAsync(point.Id, f.Caller, default);
        await f.SetLiveStateAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<CheckpointException>(() => f.Service.RestoreAsync(point.Id,
            new(preview.Huella, "RESTAURAR TODO"), f.Caller, default))).Status);
        await f.AssertLiveAsync(epoch);
        preview = await f.Service.PreviewAsync(point.Id, f.Caller, default);
        await File.WriteAllBytesAsync(f.Upload, [7, 7, 7, 7]);
        Assert.Equal(409, (await Assert.ThrowsAsync<CheckpointException>(() => f.Service.RestoreAsync(point.Id,
            new(preview.Huella, "RESTAURAR TODO"), f.Caller, default))).Status);
        Assert.Equal(new byte[] { 7, 7, 7, 7 }, await File.ReadAllBytesAsync(f.Upload));
        Assert.Equal(epoch, f.Epoch.Current);
        Assert.Single(f.Storage.Ids()); // Staleness is detected before pre-backup or file swap.
        Assert.False(await f.Recovery.HasRestoredAsync());
    }

    [SnapshotPostgresFact]
    public async Task ExternalUnmappedForeignKeyRefusesTruncateAndRecoversDatabaseAndFiles()
    {
        await using var f = await DisposableDatabase.CreateAsync();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TABLE public.\"ExternalSnapshotGuard\" (\"UserId\" uuid NOT NULL REFERENCES public.\"Users\" (\"Id\"))");
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.\"ExternalSnapshotGuard\" (\"UserId\") VALUES ({f.AdminId})");
        var point = await f.Service.CreateAsync(new("External FK", null), f.Caller, default);
        await f.SetLiveStateAsync();
        var epoch = f.Epoch.Current;
        var preview = await f.Service.PreviewAsync(point.Id, f.Caller, default);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => f.Service.RestoreAsync(point.Id,
            new(preview.Huella, "RESTAURAR TODO"), f.Caller, default));
        Assert.Equal("0A000", exception.SqlState);
        await f.AssertLiveAsync(epoch);
        Assert.Equal(2, f.Storage.Ids().Count());
        Assert.False(await f.Recovery.HasRestoredAsync());
        Assert.Single(await f.Db.Tokens.ToListAsync());
        await using var command = f.Db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT count(*) FROM public.\"ExternalSnapshotGuard\"";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [SnapshotPostgresFact]
    public async Task InsertConstraintFailureRollsBackRowsSequencesFilesAndSessionEpoch()
    {
        await using var f = await DisposableDatabase.CreateAsync();
        // NOT VALID permits an existing historical row while enforcing the constraint on restored inserts.
        await f.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Users\" ADD CONSTRAINT \"IT_RejectHistoricalName\" CHECK (\"FirstName\" <> 'Snapshot admin') NOT VALID");
        var point = await f.Service.CreateAsync(new("Constraint rollback", null), f.Caller, default);
        await f.SetLiveStateAsync();
        var extra = new Alumno { Nombres = "Live", Apellidos = "Additional", NumeroDocumento = "16180339" };
        f.Db.Add(extra); await f.Db.SaveChangesAsync();
        var epoch = f.Epoch.Current;
        var preview = await f.Service.PreviewAsync(point.Id, f.Caller, default);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => f.Service.RestoreAsync(point.Id,
            new(preview.Huella, "RESTAURAR TODO"), f.Caller, default));
        Assert.Equal("23514", exception.SqlState);
        await f.AssertLiveAsync(epoch);
        Assert.Equal(2, await f.Db.Alumnos.IgnoreQueryFilters().CountAsync());
        Assert.Single(await f.Db.Calificaciones.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await f.Db.PasswordResetCodes.ToListAsync());
        Assert.False(await f.Recovery.HasRestoredAsync());
        var afterFailure = new Alumno { Nombres = "After", Apellidos = "Failure", NumeroDocumento = "14142135" };
        f.Db.Add(afterFailure); await f.Db.SaveChangesAsync();
        Assert.Equal(extra.Id + 1, afterFailure.Id); // Earlier ALTER SEQUENCE RESTARTs must roll back too.
        Assert.Equal(2, f.Storage.Ids().Count());
    }

    [SnapshotPostgresFact]
    public async Task DurableJournalRecoversRolledBackAndCommittedSwapsWithFreshRecoveryObjects()
    {
        await using var f = await DisposableDatabase.CreateAsync();
        var point = await f.Service.CreateAsync(new("Recovery boundaries", null), f.Caller, default);
        var source = f.Storage.Load(point.Id);
        await f.SetLiveStateAsync();
        var epoch = f.Epoch.Current;
        var rolledBack = Guid.NewGuid();
        f.Recovery.StageFiles(rolledBack, source);
        f.Recovery.Swap(rolledBack);
        Assert.Equal(source.Archivos["alumno-documentos/proof.bin"], await File.ReadAllBytesAsync(f.Upload));
        await using (var transaction = await f.Db.Database.BeginTransactionAsync())
        {
            await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Users\" SET \"FirstName\"='Snapshot admin' WHERE \"Id\"={f.AdminId}");
            await f.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.\"__CensCheckpointCommits\" (\"Id\") VALUES ({rolledBack})");
            await transaction.RollbackAsync();
        }
        var recoveryAfterRollback = new CheckpointRecovery(f.Db, f.Storage, new SessionEpoch(f.Storage), f.Gate);
        await recoveryAfterRollback.InitializeAsync();
        await f.AssertLiveAsync(epoch);
        var committed = Guid.NewGuid();
        f.Recovery.StageFiles(committed, source);
        f.Recovery.Swap(committed);
        await using (var transaction = await f.Db.Database.BeginTransactionAsync())
        {
            await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Users\" SET \"FirstName\"='Snapshot admin' WHERE \"Id\"={f.AdminId}");
            await f.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.\"__CensCheckpointCommits\" (\"Id\") VALUES ({committed})");
            await transaction.CommitAsync();
        }
        var startupEpoch = new SessionEpoch(f.Storage);
        var recoveryAfterCommit = new CheckpointRecovery(f.Db, f.Storage, startupEpoch, f.Gate);
        await recoveryAfterCommit.InitializeAsync();
        Assert.NotEqual(epoch, startupEpoch.Current);
        Assert.Equal(startupEpoch.Current, new SessionEpoch(f.Storage).Current);
        f.Db.ChangeTracker.Clear();
        Assert.Equal("Snapshot admin", (await f.Db.Users.SingleAsync()).FirstName);
        Assert.Equal(source.Archivos["alumno-documentos/proof.bin"], await File.ReadAllBytesAsync(f.Upload));
        Assert.False(File.Exists(Path.Combine(f.Storage.Root, "restore.journal")));
        Assert.True(f.Gate.Healthy);
        await recoveryAfterCommit.InitializeAsync();
        Assert.Equal(startupEpoch.Current, new SessionEpoch(f.Storage).Current);
    }
}
