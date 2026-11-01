using System;
using System.Collections.Generic;

namespace EndermanQa.Desktop.Results;

public sealed record SuiteEvidenceAttachment(string Path, string ExpectedSha256, string? DisplayName = null);

public sealed class SuiteEvidenceChangedEventArgs(IReadOnlyList<SuiteEvidenceAttachment> attachments) : EventArgs
{
    public IReadOnlyList<SuiteEvidenceAttachment> Attachments { get; } = attachments ?? throw new ArgumentNullException(nameof(attachments));
}
