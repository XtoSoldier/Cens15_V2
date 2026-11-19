using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CENS15_V2.Services;

public static class ApplicationStartup
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, ILogger logger,
        CancellationToken ct = default)
    {
        var readiness = services.GetRequiredService<StartupReadiness>();
        var gate = services.GetRequiredService<CheckpointGate>();
        readiness.MarkUnavailable("initializing");
        gate.Healthy = false;
        var stage = "configuration_unavailable";
        try
        {
            ValidateConfiguration(configuration);
            stage = "storage_unavailable";
            services.GetRequiredService<CheckpointStorage>();

            await using var scope = services.CreateAsyncScope();
            stage = "database_unavailable";
            using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            if (!await scope.ServiceProvider.GetRequiredService<IDatabaseSchemaProbe>().IsCompatibleAsync(probeTimeout.Token))
            {
                readiness.MarkUnavailable("schema_incompatible");
                logger.LogError("La estructura PostgreSQL no es compatible. Ejecute mantenimiento migrate explicitamente; no se modifico la base al iniciar.");
                return;
            }

            // Recovery may finish a journaled file swap, but never creates schema here.
            stage = "recovery_pending";
            await scope.ServiceProvider.GetRequiredService<CheckpointRecovery>().InitializeAsync(ct, prepareDatabase: false);
            readiness.MarkReady();
            logger.LogInformation("API preparada: estructura compatible y recuperacion verificada, sin migraciones ni seed automaticos.");
        }
        catch (Exception ex)
        {
            gate.Healthy = false;
            readiness.MarkUnavailable(stage);
            logger.LogError(ex, "API no preparada en fase {Stage}. Salud y Swagger permanecen disponibles; datos bloqueados con 503.", stage);
        }
    }

    private static void ValidateConfiguration(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection.");
        var target = new NpgsqlConnectionStringBuilder(connection);
        if (string.IsNullOrWhiteSpace(target.Host) || string.IsNullOrWhiteSpace(target.Database))
            throw new InvalidOperationException("La conexion debe indicar Host y Database explicitamente.");
        var jwt = configuration.GetSection("Jwt");
        if (string.IsNullOrWhiteSpace(jwt["Key"]) || Encoding.UTF8.GetByteCount(jwt["Key"]!) < 32 ||
            string.IsNullOrWhiteSpace(jwt["Issuer"]) || string.IsNullOrWhiteSpace(jwt["Audience"]) ||
            !double.TryParse(jwt["DurationInMinutes"], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ||
            !double.IsFinite(duration) || duration <= 0)
            throw new InvalidOperationException("Configuracion JWT incompleta o invalida; se requiere una clave de al menos 32 bytes.");
        _ = DateTime.UtcNow.AddMinutes(duration);
        if (!int.TryParse(configuration["Security:BCryptWorkFactor"], out var cost) || cost is < 4 or > 31)
            throw new InvalidOperationException("Security:BCryptWorkFactor debe estar entre 4 y 31.");
    }
}
