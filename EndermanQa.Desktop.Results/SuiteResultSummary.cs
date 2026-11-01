using System;
using System.Collections.Generic;
using System.Linq;

namespace EndermanQa.Desktop.Results;

public sealed record SuiteResultSummaryCounts(int Total, int Attempted, int FullPass, int Partial, int Failures, int Inconclusive, int NotRun)
{
    public string ToDisplayText() =>
        $"전체 {Total} · 실행 {Attempted} · 미실행 {NotRun} · 추가 확인 {Inconclusive + NotRun}{Environment.NewLine}" +
        $"완전 통과 {FullPass} · 부분 확인 {Partial} · 실패 {Failures} · 미확정 {Inconclusive}";
}

public static class SuiteResultSummaryBuilder
{
    public static SuiteResultSummaryCounts Build(IEnumerable<SuiteCaseRow> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rows = source.ToArray();
        var fullPass = 0;
        var partial = 0;
        var failures = 0;
        var inconclusive = 0;
        var notRun = 0;

        foreach (var row in rows)
        {
            var status = row.Status ?? string.Empty;
            var verdict = row.Verdict ?? string.Empty;
            var isFailure = verdict.Equals("Fail", StringComparison.OrdinalIgnoreCase)
                || status.StartsWith("failure-", StringComparison.OrdinalIgnoreCase);            var isInconclusive = verdict.Equals("Inconclusive", StringComparison.OrdinalIgnoreCase)
                || (status.Equals("anomaly-unconfirmed", StringComparison.OrdinalIgnoreCase)
                    && !verdict.Equals("Pass", StringComparison.OrdinalIgnoreCase));
            var isPartial = status.Equals("executed-partial", StringComparison.OrdinalIgnoreCase)
                || (status.Equals("anomaly-unconfirmed", StringComparison.OrdinalIgnoreCase)
                    && verdict.Equals("Pass", StringComparison.OrdinalIgnoreCase));
            var isExecuted = status.StartsWith("executed-", StringComparison.OrdinalIgnoreCase)
                || status.StartsWith("failure-", StringComparison.OrdinalIgnoreCase)
                || status.Equals("anomaly-unconfirmed", StringComparison.OrdinalIgnoreCase);

            if (!isExecuted)
            {
                notRun++;
            }
            if (isFailure)
            {
                failures++;
            }
            else if (isInconclusive)
            {
                inconclusive++;
            }
            else if (isPartial)
            {
                partial++;
            }
            else if (status.Equals("executed-pass", StringComparison.OrdinalIgnoreCase)
                && verdict.Equals("Pass", StringComparison.OrdinalIgnoreCase))
            {
                fullPass++;
            }
        }

        return new SuiteResultSummaryCounts(rows.Length, rows.Length - notRun, fullPass, partial, failures, inconclusive, notRun);
    }
}
