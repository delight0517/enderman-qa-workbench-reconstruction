using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EndermanQa.Desktop.Integrations;

public sealed record QaPortfolioExportResult(string FolderPath, string ZipPath, int CaseCount, int IncludedEvidenceCount, string ManifestSha256, string GuidanceSource);

/// <summary>Builds a self-contained, local QA portfolio using project submission notes as guidance.</summary>
public static class QaPortfolioExportService
{
    private const long MaxEvidenceFileBytes = 100L * 1024 * 1024;
    private const long MaxEvidenceTotalBytes = 500L * 1024 * 1024;
    private static readonly HashSet<string> EvidenceExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jsonl", ".json", ".png", ".jpg", ".jpeg", ".webp", ".mp4", ".webm", ".pdf" };
    private static readonly string[] GuidanceFiles = { "SUBMISSION.md", "QA-PORTFOLIO.md", "README.md", "PROJECT-README.md", "QA-TARGET.md" };
    private static readonly Regex GuidanceTerm = new("submit|submission|portfolio|deliver|report|evidence|format|export|제출|포트폴리오|보고서|증거|형식|내보내기", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AbsolutePath = new(@"(?i)\b[A-Z]:\\[^\r\n;]+", RegexOptions.Compiled);

    public static QaPortfolioExportResult Export(string projectRoot, IEnumerable<SuiteQaExportRow> source)
    {
        var root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("선택한 프로젝트 폴더를 찾을 수 없습니다.");
        var rows = source.OrderBy(r => r.CaseNumber, StringComparer.Ordinal).ToArray();
        if (rows.Length == 0) throw new InvalidOperationException("내보낼 QA 케이스가 없습니다.");
        var guidance = ReadGuidance(root);
        var exports = Path.Combine(root, "exports");
        Directory.CreateDirectory(exports);
        var operationId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N");
        var folder = Path.Combine(exports, "QA-Portfolio-" + operationId);
        var zipPath = Path.Combine(exports, "QA-Portfolio-" + operationId + ".zip");
        if (Directory.Exists(folder) || File.Exists(folder) || File.Exists(zipPath) || Directory.Exists(zipPath))
            throw new IOException("내보내기 작업 ID가 이미 사용 중입니다. 다시 시도하세요.");
        Directory.CreateDirectory(folder);
        try
        {
            var evidenceRoot = Path.Combine(folder, "evidence");
            Directory.CreateDirectory(evidenceRoot);
            var issues = new List<string>();
            var exportedRows = new List<SuiteQaExportRow>(rows.Length);
            long copiedBytes = 0;
            int includedEvidence = 0;
            foreach (var row in rows)
            {
                var journals = CopyEvidence(row.JournalPath, row.CaseNumber, "journal", root, evidenceRoot, issues, ref copiedBytes, ref includedEvidence);
                var media = CopyEvidence(row.MediaPaths, row.CaseNumber, "capture", root, evidenceRoot, issues, ref copiedBytes, ref includedEvidence);
                exportedRows.Add(row with
                {
                    CaseNumber = SanitizeText(row.CaseNumber, root), CaseId = SanitizeText(row.CaseId, root), Title = SanitizeText(row.Title, root),
                    Status = SanitizeText(row.Status, root), Verdict = SanitizeText(row.Verdict, root), Coverage = SanitizeText(row.Coverage, root),
                    Attempts = SanitizeText(row.Attempts, root), Repeatability = SanitizeText(row.Repeatability, root), SuiteId = SanitizeText(row.SuiteId, root),
                    CaptureStatus = SanitizeText(row.CaptureStatus, root),
                    SourceCandidate = SanitizeText(row.SourceCandidate, root),
                    FollowUpGuidance = SanitizeText(row.FollowUpGuidance, root),
                    Preconditions = SanitizeText(row.Preconditions, root), ExpectedActions = SanitizeText(row.ExpectedActions, root),
                    ExpectedObservation = SanitizeText(row.ExpectedObservation, root), PassCriteria = SanitizeText(row.PassCriteria, root),
                    Actual = SanitizeText(row.Actual, root), Limitation = SanitizeText(row.Limitation, root),
                    JournalPath = string.Join(Environment.NewLine, journals), MediaPaths = string.Join(Environment.NewLine, media)
                });
            }
            var excel = Path.Combine(folder, "QA-Portfolio.xlsx");
            ExportServices.ExportSuiteResultsExcel(excel, exportedRows, folder, relativeEvidencePaths: true);
            ExportServices.ExportSuiteResultsCsv(Path.Combine(folder, "Test-Cases.csv"), exportedRows);
            WriteText(Path.Combine(folder, "Portfolio-README.md"), BuildReadme(rows, includedEvidence, guidance));
            WriteText(Path.Combine(folder, "Submission-Guidance.md"), guidance.Text);
            WriteText(Path.Combine(folder, "Evidence-Index.csv"), BuildEvidenceCsv(exportedRows));
            WriteText(Path.Combine(folder, "Export-Notes.md"), issues.Count == 0 ? "모든 포함 대상 증거 파일을 번들에 복사했습니다." : "포함하지 못한 증거 파일:\n\n" + string.Join("\n", issues.Select(i => "- " + i)));

            var payload = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).Select(path => new
            {
                path = Path.GetRelativePath(folder, path).Replace('\\', '/'),
                size = new FileInfo(path).Length,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            }).ToArray();
            var manifestPath = Path.Combine(folder, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { schemaVersion = 1, createdLocal = DateTimeOffset.Now, caseCount = rows.Length, includedEvidenceCount = includedEvidence, guidanceSource = guidance.Source, files = payload }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(true));
            ZipFile.CreateFromDirectory(folder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
            var manifestHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath)));
            return new QaPortfolioExportResult(folder, zipPath, rows.Length, includedEvidence, manifestHash, guidance.Source);
        }
        catch
        {
            // Keep a partial local bundle for diagnosis, but never report it as a successful export.
            throw;
        }
    }

    private static (string Source, string Text) ReadGuidance(string root)
    {
        foreach (var name in GuidanceFiles)
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path)) continue;
            var info = new FileInfo(path);
            if (info.Length > 256 * 1024) continue;
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            var relevant = lines.Select((line, index) => (line, index)).Where(x => GuidanceTerm.IsMatch(x.line)).ToArray();
            var selected = new SortedSet<int>();
            foreach (var item in relevant)
                for (var i = Math.Max(0, item.index - 1); i <= Math.Min(lines.Length - 1, item.index + 1); i++) selected.Add(i);
            var text = selected.Count == 0 ? "관련 제출 지침이 감지되지 않았습니다. 기본 QA 포트폴리오 형식으로 내보냈습니다." : string.Join(Environment.NewLine, selected.Select(i => lines[i]));
            return (name, SanitizeText(text, root));
        }
        return ("기본 형식", "프로젝트 제출 안내 파일을 찾지 못했습니다. 기본 QA 포트폴리오 형식으로 내보냈습니다.");
    }

    private static List<string> CopyEvidence(string raw, string caseNumber, string kind, string root, string evidenceRoot, List<string> issues, ref long total, ref int count)
    {
        var result = new List<string>();
        foreach (var input in (raw ?? "").Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Path.IsPathFullyQualified(input)) { issues.Add($"{caseNumber}: 상대 경로 증거를 확인할 수 없어 제외됨"); continue; }
                var full = Path.GetFullPath(input);
                if (!IsWithin(root, full) || HasReparsePoint(root, full)) { issues.Add($"{caseNumber}: 프로젝트 밖 또는 링크 경로 증거를 제외함"); continue; }
                if (!EvidenceExtensions.Contains(Path.GetExtension(full))) { issues.Add($"{caseNumber}: 지원하지 않는 증거 파일 형식 제외됨"); continue; }
                if (!File.Exists(full)) { issues.Add($"{caseNumber}: 누락된 증거 파일 제외됨"); continue; }
                var info = new FileInfo(full);
                if (info.Length > MaxEvidenceFileBytes || total + info.Length > MaxEvidenceTotalBytes) { issues.Add($"{caseNumber}: 증거 크기 제한으로 제외됨"); continue; }
                var caseFolder = Path.Combine(evidenceRoot, SafeSegment(caseNumber));
                Directory.CreateDirectory(caseFolder);
                var name = $"{kind}-{result.Count + 1:00}{Path.GetExtension(full).ToLowerInvariant()}";
                var dest = Path.Combine(caseFolder, name);
                File.Copy(full, dest, overwrite: false);
                total += info.Length; count++;
                result.Add(Path.GetRelativePath(Path.GetDirectoryName(evidenceRoot)!, dest).Replace('\\', '/'));
            }
            catch (IOException) { issues.Add($"{caseNumber}: 증거 파일 읽기 실패"); }
            catch (UnauthorizedAccessException) { issues.Add($"{caseNumber}: 증거 파일 접근 불가"); }
        }
        return result;
    }

    private static bool IsWithin(string root, string path) => path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool HasReparsePoint(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        var current = root;
        foreach (var segment in rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }
    private static string SafeSegment(string value) => Regex.Replace(value, "[^A-Za-z0-9._-]", "_");
    private static string SanitizeText(string value, string root) => AbsolutePath.Replace((value ?? "").Replace(root, "[project]", StringComparison.OrdinalIgnoreCase), "[local path]");
    private static void WriteText(string path, string value) => File.WriteAllText(path, value, new UTF8Encoding(true));
    private static string BuildReadme(SuiteQaExportRow[] rows, int evidence, (string Source, string Text) guidance)
    {
        string Classify(SuiteQaExportRow row)
        {
            if (row.Verdict.Equals("Fail", StringComparison.OrdinalIgnoreCase) || row.Status.StartsWith("failure-", StringComparison.OrdinalIgnoreCase))
                return "Fail";
            if (row.Verdict.Equals("Inconclusive", StringComparison.OrdinalIgnoreCase)
                || (row.Status.Equals("anomaly-unconfirmed", StringComparison.OrdinalIgnoreCase) && !row.Verdict.Equals("Pass", StringComparison.OrdinalIgnoreCase)))
                return "Inconclusive";
            if (row.Status.Equals("executed-partial", StringComparison.OrdinalIgnoreCase)
                || (row.Status.Equals("anomaly-unconfirmed", StringComparison.OrdinalIgnoreCase) && row.Verdict.Equals("Pass", StringComparison.OrdinalIgnoreCase))
                || row.Verdict.Equals("Partial", StringComparison.OrdinalIgnoreCase)
                || (row.Verdict.Equals("Pass", StringComparison.OrdinalIgnoreCase) && row.Coverage.Equals("partial", StringComparison.OrdinalIgnoreCase)))
                return "Partial";
            if (row.Status.Equals("executed-pass", StringComparison.OrdinalIgnoreCase)
                && row.Verdict.Equals("Pass", StringComparison.OrdinalIgnoreCase)
                && !row.Coverage.Equals("partial", StringComparison.OrdinalIgnoreCase))
                return "Full pass";
            if (!row.Status.StartsWith("executed-", StringComparison.OrdinalIgnoreCase)
                && !row.Status.StartsWith("failure-", StringComparison.OrdinalIgnoreCase)
                && !row.Status.Equals("anomaly-unconfirmed", StringComparison.OrdinalIgnoreCase))
                return "Not run";
            return "Other";
        }

        var fullPass = rows.Count(r => Classify(r) == "Full pass");
        var partial = rows.Count(r => Classify(r) == "Partial");
        var failures = rows.Count(r => Classify(r) == "Fail");
        var inconclusive = rows.Count(r => Classify(r) == "Inconclusive");
        var notRun = rows.Count(r => Classify(r) == "Not run");
        return $"# QA Portfolio\n\n- Cases: {rows.Length}\n- Included evidence files: {evidence}\n- Submission guidance source: {guidance.Source}\n- Full pass: {fullPass}\n- Partial: {partial}\n- Failures: {failures}\n- Inconclusive: {inconclusive}\n- Not run: {notRun}\n\nSee `Test-Cases.csv`, `QA-Portfolio.xlsx`, `Evidence-Index.csv`, and `Submission-Guidance.md`.\n";
    }
    private static string BuildEvidenceCsv(IEnumerable<SuiteQaExportRow> rows)
    {
        var lines = new List<string> { "Case #,Case ID,Title,Journal paths,Media paths" };
        lines.AddRange(rows.Select(r => string.Join(",", new[] { r.CaseNumber, r.CaseId, r.Title, r.JournalPath, r.MediaPaths }.Select(Csv))));
        return string.Join("\r\n", lines);
    }
    private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
