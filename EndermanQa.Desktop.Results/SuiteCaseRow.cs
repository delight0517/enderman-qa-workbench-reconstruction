namespace EndermanQa.Desktop.Results;

public sealed record SuiteCaseRow(int SourceId, string CaseId, string Title, string Status, string Verdict, string Coverage, string ActualObservation, int Attempts, string Repeatability, string Signature, string Limitation, string CaptureStatus, string JournalPath, string SuiteId, string SourceCandidate, string Preconditions, string ExpectedActions, string ExpectedObservation, string PassCriteria, string Capability, string PlanStatus)
{
	public string FollowUpGuidance { get; init; } = "";

	public string IdDisplay => $"#{SourceId:000}";

	public string StatusDisplay
	{
		get
		{
			switch (Status)
			{
			case "executed-pass":
				return "실행 · 통과";
			case "executed-partial":
				return "실행 · 부분 확인";
			case "anomaly-unconfirmed":
				return "실행 · 미확정";
			case "failure-reproduced":
				return "실행 · 반복 재현";
			case "failure-confirmed":
				return "실행 · 실패 확인";
			case "not-run-capability":
				return "미실행 · 기능 미지원";
			case "planned-not-run":
			case "":
				return "미실행 · 계획만 있음";
			default:
				return Status;
			}
		}
	}

	public string VerdictDisplay
	{
		get
		{
			if (Verdict.Equals("Pass", System.StringComparison.OrdinalIgnoreCase)
				&& Coverage.Equals("partial", System.StringComparison.OrdinalIgnoreCase))
				return "부분 확인";

			string verdict = Verdict;
			switch (verdict)
			{
			default:
				if (verdict.Length != 0)
				{
					break;
				}
				return "미판정";
			case "Pass":
				return "통과";
			case "Fail":
				return "실패";
			case "Inconclusive":
				return "미확정";
			case "NotRun":
				return "미실행";
			case null:
				break;
			}
			return Verdict;
		}
	}
}
