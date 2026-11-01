using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;

namespace EndermanQa.Desktop.Persistence;

public sealed record PreparedTargetRecord(
    int SchemaVersion,
    string AppVersion,
    string ProjectId,
    string InstanceId,
    string InstanceRoot,
    string WorldPath,
    string GameVersion,
    string RuntimeKind,
    int ServerPort,
    string ServerBind,
    bool OnlineMode,
    string State,
    bool EulaAccepted,
    bool WorldCreated,
    string ServerJarSha256,
    string FabricApiSha256,
    string BridgeJarSha256,
    string? JavaRuntimePath,
    string? JavaVersion,
    DateTimeOffset RecordedAtUtc);

public sealed class PreparedTargetRecordStore
{
    // The existing QA-TARGET-CURRENT.json is an established project manifest owned by
    // the current app/data pipeline. Keep it read-compatible and write recovery state
    // beside it so a Workbench update never replaces fields it does not understand.
    public const string LegacyFileName = "QA-TARGET-CURRENT.json";
    public const string RecoveryFileName = "QA-TARGET-RECOVERY.json";
    private const int MaximumBytes = 65536;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PreparedTargetRecord? Read(string projectRoot)
    {
        string root = ResolveSafeProjectRoot(projectRoot);
        string path = Path.Combine(root, RecoveryFileName);
        if (!File.Exists(path)) path = Path.Combine(root, LegacyFileName);
        if (!File.Exists(path)) return null;
        RejectReparsePoint(path);
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > MaximumBytes) throw new InvalidDataException("Prepared target record size is outside the allowed limit.");
        PreparedTargetRecord? record;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            record = JsonSerializer.Deserialize<PreparedTargetRecord>(stream, Options);
        if (record is null) throw new InvalidDataException("Prepared target record is empty or invalid JSON.");
        Validate(record);
        return record;
    }

    public void Write(string projectRoot, PreparedTargetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Validate(record);
        string root = ResolveSafeProjectRoot(projectRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Project root must exist before saving its prepared target record.");
        string destination = Path.Combine(root, RecoveryFileName);
        if (File.Exists(destination)) RejectReparsePoint(destination);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(record, Options);
            if (bytes.Length > MaximumBytes) throw new InvalidDataException("Prepared target record exceeds the allowed size.");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            throw;
        }
    }

    private static void Validate(PreparedTargetRecord record)
    {
        if (record.SchemaVersion != 1 || string.IsNullOrWhiteSpace(record.ProjectId) || record.ProjectId.Length > 160)
            throw new InvalidDataException("Prepared target record has an unsupported schema or project identity.");
        if (string.IsNullOrWhiteSpace(record.InstanceId) || record.InstanceId.Length != 32 || !Guid.TryParseExact(record.InstanceId, "N", out _))
            throw new SecurityException("Prepared target record must identify a generated instance UUID.");
        if (string.IsNullOrWhiteSpace(record.InstanceRoot) || string.IsNullOrWhiteSpace(record.WorldPath)
            || !Path.IsPathFullyQualified(record.InstanceRoot) || !Path.IsPathFullyQualified(record.WorldPath))
            throw new InvalidDataException("Prepared target paths must be absolute.");
        var validState = record.State is "prepared-not-started" or "eula-accepted-not-started" or "server-started" or "world-ready";
		if (!validState || (record.State == "prepared-not-started" && (record.EulaAccepted || record.WorldCreated))
			|| (record.State == "eula-accepted-not-started" && (!record.EulaAccepted || record.WorldCreated))
            || (record.State != "prepared-not-started" && !record.EulaAccepted)
            || record.ServerBind != "127.0.0.1" || record.OnlineMode || record.ServerPort is < 1024 or > 65535)
            throw new SecurityException("Prepared target record violates the stopped, EULA-gated private-target contract.");
        if (record.GameVersion != "26.3" || record.RuntimeKind != "Fabric dedicated server")
            throw new InvalidDataException("Prepared target record names an unsupported runtime.");
        if (!IsSha256(record.ServerJarSha256) || !IsSha256(record.FabricApiSha256) || !IsSha256(record.BridgeJarSha256))
            throw new InvalidDataException("Prepared target artifact hashes must be SHA-256 values.");
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string ResolveSafeProjectRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("Project root is required.", nameof(projectRoot));
        string root = Path.GetFullPath(projectRoot);
        if (Directory.Exists(root)) RejectReparsePoint(root);
        return root;
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new SecurityException("Prepared target record path is a reparse point; refusing to follow it.");
    }
}
