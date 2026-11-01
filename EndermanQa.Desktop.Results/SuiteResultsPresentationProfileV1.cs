using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EndermanQa.Desktop.Results;

/// <summary>
/// A data-only presentation module. This file may tune the compiled results layout,
/// but it cannot load code, XAML, resources, paths, or event handlers.
/// </summary>
internal sealed record SuiteResultsPresentationProfileV1(
    string ModuleId,
    int ApiVersion,
    string Version,
    int CompactTabsThresholdPx,
    int HeaderStackThresholdPx,
    int CompactRowHeightPx,
    int RegularRowHeightPx,
    int PageMarginPx,
    int BodyGapPx)
{
    public const string ExpectedModuleId = "qa.results.presentation";
    public const int ExpectedApiVersion = 1;
    public const string DefaultVersion = "1.2.0";
    public const string FileName = "qa.results.presentation.v1.json";

    public static SuiteResultsPresentationProfileV1 Default { get; } = new(
        ExpectedModuleId, ExpectedApiVersion, DefaultVersion,
        CompactTabsThresholdPx: 820,
        HeaderStackThresholdPx: 1060,
        CompactRowHeightPx: 40,
        RegularRowHeightPx: 36,
        PageMarginPx: 16,
        BodyGapPx: 14);

    public static string DefaultHash => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Default, JsonOptions)));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public string Validate()
    {
        if (!string.Equals(ModuleId, ExpectedModuleId, StringComparison.Ordinal))
            throw new InvalidDataException("Presentation profile module ID is not supported.");
        if (ApiVersion != ExpectedApiVersion)
            throw new InvalidDataException($"Presentation profile API {ApiVersion} is not supported.");
        if (string.IsNullOrWhiteSpace(Version) || Version.Length > 32 || Version.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+')))
            throw new InvalidDataException("Presentation profile version is invalid.");
        if (CompactTabsThresholdPx is < 600 or > 1100)
            throw new InvalidDataException("Compact-tabs threshold must be between 600 and 1100 pixels.");
        if (HeaderStackThresholdPx < CompactTabsThresholdPx || HeaderStackThresholdPx > 1500)
            throw new InvalidDataException("Header threshold must be at least the compact-tabs threshold and no more than 1500 pixels.");
        if (CompactRowHeightPx is < 30 or > 60 || RegularRowHeightPx is < 30 or > 60)
            throw new InvalidDataException("Results row height must be between 30 and 60 pixels.");
        if (PageMarginPx is < 4 or > 28 || BodyGapPx is < 4 or > 28)
            throw new InvalidDataException("Results spacing must be between 4 and 28 pixels.");
        return string.Empty;
    }

    public static string ResolvePath(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        string root = Path.GetFullPath(appDataRoot);
        return Path.GetFullPath(Path.Combine(root, "modules", FileName));
    }

    public static (SuiteResultsPresentationProfileV1 Profile, string Sha256, string Path) LoadOrCreate(string appDataRoot)
    {
        string root = Path.GetFullPath(appDataRoot);
        RejectReparseAncestors(root);
        string directory = Path.Combine(root, "modules");
        Directory.CreateDirectory(directory);
        RejectReparseAncestors(directory);

        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            byte[] initial = JsonSerializer.SerializeToUtf8Bytes(Default, JsonOptions);
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
                stream.Write(initial);
                stream.Flush(flushToDisk: true);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another app instance created the same default file first.
            }
        }

        RejectReparseAncestors(path);
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Presentation profile file cannot be a reparse point.");
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > 32768)
            throw new InvalidDataException("Presentation profile must contain between 1 and 32768 bytes.");
        byte[] bytes = File.ReadAllBytes(path);
        var profile = JsonSerializer.Deserialize<SuiteResultsPresentationProfileV1>(bytes, JsonOptions)
            ?? throw new InvalidDataException("Presentation profile is empty.");
        profile.Validate();
        return (profile, Convert.ToHexString(SHA256.HashData(bytes)), path);
    }

    private static void RejectReparseAncestors(string path)
    {
        string current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current) || File.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Presentation profile path contains a reparse point.");
            }
            string? parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
            current = parent ?? string.Empty;
        }
    }
}
