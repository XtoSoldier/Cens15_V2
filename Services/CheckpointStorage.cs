using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CENS15_V2.Models;

namespace CENS15_V2.Services;

public sealed class CheckpointStorage : IDisposable
{
    public const int MaximumBytes = 128 * 1024 * 1024;
    private static readonly byte[] Magic = "CENSBK01"u8.ToArray();
    public static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 40
    };
    public string Root { get; }
    public string Uploads { get; }
    private readonly byte[] key;
    private readonly FileStream instanceLock;

    public CheckpointStorage(IConfiguration config, IWebHostEnvironment env)
    {
        Root = Path.GetFullPath(config["Snapshots:Directory"] ?? Path.Combine(env.ContentRootPath, "App_Data", "checkpoints"));
        var web = Path.GetFullPath(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"));
        Uploads = Path.Combine(web, "uploads");
        if (Within(Root, web) || Within(web, Root)) throw new InvalidOperationException("Snapshots:Directory debe estar fuera de wwwroot y no contenerlo.");
        EnsureNoLinks(Root);
        EnsureNoLinks(Uploads);
        Directory.CreateDirectory(Root);
        // A missing uploads directory during recovery is evidence, not an empty directory to recreate.
        if (!File.Exists(Path.Combine(Root, "restore.journal"))) Directory.CreateDirectory(Uploads);
        RestrictDirectory(Root);
        var lockPath = Path.Combine(Root, "instance.lock");
        EnsureNoLinks(lockPath);
        instanceLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var keyPath = Path.Combine(Root, "server.key");
            if (!File.Exists(keyPath) && (Directory.EnumerateFiles(Root, "*.checkpoint").Any() || File.Exists(Path.Combine(Root, "restore.journal"))))
                throw new InvalidOperationException("Falta la clave persistente de checkpoints. Recupere server.key antes de iniciar.");
            if (!File.Exists(keyPath)) AtomicWrite(keyPath, RandomNumberGenerator.GetBytes(32));
            key = File.ReadAllBytes(keyPath);
            if (key.Length != 32) throw new InvalidOperationException("Clave de checkpoints invalida.");
        }
        catch
        {
            instanceLock.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(key);
        instanceLock.Dispose();
    }

    public static bool Within(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void EnsureNoLinks(string path)
    {
        for (var p = Path.GetFullPath(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new CheckpointException("No se permiten enlaces simbolicos en el almacenamiento de checkpoints/uploads.");
    }

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static void AtomicWrite(string path, byte[] bytes)
    {
        EnsureNoLinks(path);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public string PathFor(Guid id) => Path.Combine(Root, id.ToString("D") + ".checkpoint");
    public IEnumerable<Guid> Ids() => Directory.EnumerateFiles(Root, "*.checkpoint").Select(Path.GetFileNameWithoutExtension)
        .Select(x => Guid.TryParse(x, out var id) ? id : throw new CheckpointException("Registro de checkpoints invalido.", 409));

    public ApplicationSnapshot Load(Guid id)
    {
        var path = PathFor(id);
        EnsureNoLinks(path);
        if (!File.Exists(path)) throw new CheckpointException("Checkpoint no encontrado.", 404);
        if (new FileInfo(path).Length > MaximumBytes) throw new CheckpointException("Checkpoint excede el limite.");
        var snapshot = Decode(Decrypt(File.ReadAllBytes(path), key));
        if (snapshot.Id != id) throw new CheckpointException("Identidad de checkpoint invalida.", 409);
        return snapshot;
    }

    public CheckpointMetadata Save(ApplicationSnapshot snapshot)
    {
        var bytes = Encode(snapshot);
        if (File.Exists(PathFor(snapshot.Id))) throw new CheckpointException("El checkpoint ya existe.", 409);
        AtomicWrite(PathFor(snapshot.Id), Encrypt(bytes, key));
        return Metadata(snapshot);
    }

    public CheckpointMetadata Metadata(ApplicationSnapshot s) => new(s.Id, s.Nombre, s.Descripcion, s.CreadoEn, s.CreadoPor,
        s.Automatico, s.Entidades.Select(x => new CheckpointTable(x.Key, x.Value.Count)).ToArray(), s.Archivos.Count,
        new FileInfo(PathFor(s.Id)).Length, s.HuellaEsquema);

    public static byte[] Encode(ApplicationSnapshot snapshot)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, Json);
        if (bytes.Length > MaximumBytes - 64) throw new CheckpointException("Snapshot demasiado grande (limite 128 MiB).");
        return bytes;
    }

    public static ApplicationSnapshot Decode(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new CheckpointException("Snapshot demasiado grande.");
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 40 });
            RejectDuplicates(doc.RootElement);
            return JsonSerializer.Deserialize<ApplicationSnapshot>(bytes, Json) ?? throw new JsonException();
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
        { throw new CheckpointException("Formato JSON de snapshot invalido."); }
    }

    private static void RejectDuplicates(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in e.EnumerateObject())
            {
                if (!names.Add(p.Name)) throw new JsonException();
                RejectDuplicates(p.Value);
            }
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) RejectDuplicates(item);
    }

    public static byte[] Export(ApplicationSnapshot snapshot, string passphrase)
    {
        CheckPassphrase(passphrase);
        var salt = RandomNumberGenerator.GetBytes(16);
        var derived = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, 600_000, HashAlgorithmName.SHA256, 32);
        try { return Encrypt(Encode(snapshot), derived, salt); }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }

    public static ApplicationSnapshot Import(byte[] archive, string passphrase)
    {
        CheckPassphrase(passphrase);
        CheckEnvelope(archive);
        var derived = Rfc2898DeriveBytes.Pbkdf2(passphrase, archive.AsSpan(8, 16), 600_000, HashAlgorithmName.SHA256, 32);
        try { return Decode(Decrypt(archive, derived)); }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }

    private static void CheckPassphrase(string? value)
    {
        if (value is null || value.Length < 12 || value.Length > 1024)
            throw new CheckpointException("La clave debe tener entre 12 y 1024 caracteres.");
    }

    private static void CheckEnvelope(byte[] bytes)
    {
        if (bytes.Length < 52 || bytes.Length > MaximumBytes || !bytes.AsSpan(0, 8).SequenceEqual(Magic))
            throw new CheckpointException("Archivo .censbackup invalido, version no admitida o limite excedido.");
    }

    private static byte[] Encrypt(byte[] plaintext, byte[] encryptionKey, byte[]? salt = null)
    {
        var result = new byte[52 + plaintext.Length];
        Magic.CopyTo(result, 0);
        (salt ?? RandomNumberGenerator.GetBytes(16)).CopyTo(result, 8);
        RandomNumberGenerator.Fill(result.AsSpan(24, 12));
        using var aes = new AesGcm(encryptionKey, 16);
        aes.Encrypt(result.AsSpan(24, 12), plaintext, result.AsSpan(52), result.AsSpan(36, 16), result.AsSpan(0, 36));
        return result;
    }

    private static byte[] Decrypt(byte[] bytes, byte[] encryptionKey)
    {
        CheckEnvelope(bytes);
        var result = new byte[bytes.Length - 52];
        try
        {
            using var aes = new AesGcm(encryptionKey, 16);
            aes.Decrypt(bytes.AsSpan(24, 12), bytes.AsSpan(52), bytes.AsSpan(36, 16), result, bytes.AsSpan(0, 36));
            return result;
        }
        catch (CryptographicException) { throw new CheckpointException("Clave incorrecta o archivo cifrado alterado."); }
    }

    public static void ValidateFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 500 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') ||
            path.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
                p.Any(c => c < 32 || "<>\"|?*".Contains(c)) ||
                new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(p.Split('.')[0], StringComparer.OrdinalIgnoreCase)))
            throw new CheckpointException("Ruta de uploads invalida.");
    }

    public SortedDictionary<string, byte[]> CaptureFiles()
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        void Walk(string dir)
        {
            EnsureNoLinks(dir);
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                EnsureNoLinks(entry);
                if (Directory.Exists(entry)) { Walk(entry); continue; }
                var name = Path.GetRelativePath(Uploads, entry).Replace('\\', '/');
                ValidateFileName(name);
                if (!seen.Add(name)) throw new CheckpointException("Rutas de uploads duplicadas (mayusculas/minusculas).");
                total += new FileInfo(entry).Length;
                if (total > MaximumBytes * 3L / 4 || files.Count >= 50_000) throw new CheckpointException("Uploads excede el limite de snapshot.");
                files.Add(name, File.ReadAllBytes(entry));
            }
        }
        Walk(Uploads);
        return files;
    }
}
