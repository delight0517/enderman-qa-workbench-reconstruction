namespace EndermanQa.Desktop.Results;

public sealed record SuiteAttempt(string RunId, int AttemptNumber, string JournalPath, string Summary)
{
	public string AttemptDisplay => $"시도 {AttemptNumber} · {RunId}";
}
