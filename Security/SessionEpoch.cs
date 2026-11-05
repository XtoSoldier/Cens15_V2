using System.Security.Cryptography;
using CENS15_V2.Services;

namespace CENS15_V2.Security;

public sealed class SessionEpoch
{
    public const string Claim = "session_epoch";
    private readonly string path;
    private string current;
    public string Current => Volatile.Read(ref current);

    public SessionEpoch(CheckpointStorage storage)
    {
        path = Path.Combine(storage.Root, "session.epoch");
        CheckpointStorage.EnsureNoLinks(path);
        if (!File.Exists(path)) CheckpointStorage.AtomicWrite(path, System.Text.Encoding.ASCII.GetBytes(NewEpoch()));
        current = File.ReadAllText(path);
        if (current.Length != 64 || !current.All(Uri.IsHexDigit)) throw new InvalidOperationException("Version de sesiones invalida.");
    }

    private static string NewEpoch() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public void Rotate()
    {
        var next = NewEpoch();
        CheckpointStorage.AtomicWrite(path, System.Text.Encoding.ASCII.GetBytes(next));
        Volatile.Write(ref current, next);
    }
}
