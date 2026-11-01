using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EndermanQa.Desktop.Results;

/// <summary>Read-only adapter for optional case-level continuation notes in the selected project checkpoint.</summary>
internal static class SuiteFollowUpGuidance
{
    internal const int ApiVersion = 1;
    private const long MaximumCheckpointBytes = 1_048_576;

    internal static IReadOnlyDictionary<int, string> Read(string projectRoot, string projectDataRoot)
    {
        var path = Path.Combine(projectRoot, "CURRENT-QA-STATUS.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(projectDataRoot, "CURRENT-QA-STATUS.json");
        }

        if (!File.Exists(path))
        {
            return new Dictionary<int, string>();
        }

        try
        {
            var file = new FileInfo(path);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length <= 0 || file.Length > MaximumCheckpointBytes)
            {
                return new Dictionary<int, string>();
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length <= 0 || stream.Length > MaximumCheckpointBytes)
            {
                return new Dictionary<int, string>();
            }

            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("remainingWork", out var remainingWork)
                || remainingWork.ValueKind != JsonValueKind.Array)
            {
                return new Dictionary<int, string>();
            }

            var guidance = new Dictionary<int, string>();
            foreach (var item in remainingWork.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("sourceId", out var sourceIdElement)
                    || !sourceIdElement.TryGetInt32(out var sourceId)
                    || sourceId <= 0
                    || !item.TryGetProperty("gap", out var gapElement)
                    || gapElement.ValueKind != JsonValueKind.String
                    || !item.TryGetProperty("existingEvidence", out var evidenceElement)
                    || evidenceElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var gap = gapElement.GetString()?.Trim();
                var evidence = evidenceElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(gap) || string.IsNullOrWhiteSpace(evidence))
                {
                    continue;
                }

                guidance[sourceId] = $"다음 확인: {gap}{Environment.NewLine}현재 기록: {evidence}";
            }

            return guidance;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<int, string>();
        }
    }
}
