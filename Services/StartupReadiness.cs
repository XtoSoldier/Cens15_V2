namespace CENS15_V2.Services;

public sealed class StartupReadiness
{
    // A single atomic reference keeps the status and its public reason together.
    private string? unavailableReason = "initializing";

    public bool IsReady => Volatile.Read(ref unavailableReason) is null;
    public string Reason => Volatile.Read(ref unavailableReason) ?? "initializing";

    public void MarkReady() => Volatile.Write(ref unavailableReason, null);

    public void MarkUnavailable(string reason) => Volatile.Write(ref unavailableReason, reason switch
    {
        "database_unavailable" or "schema_incompatible" or "storage_unavailable"
            or "recovery_pending" or "configuration_unavailable" => reason,
        _ => "initializing"
    });
}
