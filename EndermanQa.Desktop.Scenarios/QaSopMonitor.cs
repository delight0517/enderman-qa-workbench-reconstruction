using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Scenarios;

public static class QaSopMonitor
{
	public sealed record Finding(string Rule, int[] Cases, string Recommendation);

	public static IReadOnlyList<Finding> Analyze(IReadOnlyList<SuiteCaseReceipt> receipts)
	{
		IReadOnlyList<SuiteCaseReceipt> receipts2 = receipts;
		List<Finding> findings = new List<Finding>();
		Add("fixture-before-repeat", (SuiteCaseReceipt r) => r.Status == "setup-inconclusive" || SuiteRetryPolicy.IsInvalidSetup(r.Limitation), "환경 오류는 게임 결함으로 확정하지 않는다. 물·바닥·충돌·AI·중력·공격 허용 목록을 읽어 확인하고, 원인을 수정한 뒤 같은 월드에서 다시 실행한다.");
		Add("inconclusive-needs-evidence", (SuiteCaseReceipt r) => r.Verdict == "Inconclusive", "같은 미확정을 반복하기 전에 투사체 충돌, 정확한 엔티티 UUID, 피해 이벤트와 관측 시간의 누락을 확인한다. 미확정을 결함 재현으로 집계하지 않는다.");
		Add("bounded-coverage", (SuiteCaseReceipt r) => r.Status == "executed-partial", "통과한 세부 조건과 제외 조건을 함께 기록한다. 제한된 고정 환경의 결과를 전체 자연 행동 검증으로 확대하지 않는다.");
		Add("capability-first", (SuiteCaseReceipt r) => r.Status == "not-run-capability", "서버 실행 전에 필요한 행동과 판정기의 지원 여부를 확인한다. 같은 어댑터 기능을 쓰는 미실행 케이스부터 묶어 구현한다.");
		Add("visual-evidence-gap", (SuiteCaseReceipt r) => r.FirstRunId != null && r.CaptureStatus.StartsWith("unavailable", StringComparison.OrdinalIgnoreCase), "서버 로그와 실제 플레이 화면은 별도 증거다. 결함 제출 전 허용된 클라이언트 캡처 경로를 확인하고, 이미지·영상이 없으면 누락으로 명시한다.");
		Add("repeat-cost", delegate(SuiteCaseReceipt r)
		{
			bool flag = r.RepeatRunIds.Count > 0;
			if (flag)
			{
				string status = r.Status;
				bool flag2 = ((status == "setup-inconclusive" || status == "anomaly-unconfirmed") ? true : false);
				flag = flag2;
			}
			return flag;
		}, "반복 시도의 서명과 준비 상태를 비교한다. 준비 실패가 같으면 원인 수정 전 재시도를 중단하고, 조건을 바꾸면 변경 내용을 남긴다.");
		return findings;
		void Add(string rule, Func<SuiteCaseReceipt, bool> predicate, string advice)
		{
			int[] array = (from x in receipts2.Where(predicate)
				select x.SourceId).Distinct().Order().ToArray();
			if (array.Length != 0)
			{
				findings.Add(new Finding(rule, array, advice));
			}
		}
	}

	public static async Task WriteAsync(string suiteRoot, IReadOnlyList<SuiteCaseReceipt> receipts)
	{
		IReadOnlyList<Finding> findings = Analyze(receipts);
		var value = new
		{
			schemaVersion = 1,
			trigger = "case-checkpoint",
			scope = "current-suite-only",
			reviewRequired = true,
			generatedAt = DateTimeOffset.UtcNow,
			casesReviewed = receipts.Count,
			findings = findings
		};
		await AtomicWrite(Path.Combine(suiteRoot, "sop-review.json"), JsonSerializer.Serialize(value, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
		StringBuilder stringBuilder = new StringBuilder("# QA SOP 자동 점검 초안\n\n실행 결과에서 생성한 검토 제안입니다. 테스트 기준이나 게임 설정을 자동 변경하지 않습니다.\n\n");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(55, 1, stringBuilder2);
		handler.AppendLiteral("검토 범위: 현재 실행의 완료 기록 ");
		handler.AppendFormatted(receipts.Count);
		handler.AppendLiteral("건. 원본: results.json 및 suite.jsonl.\n");
		stringBuilder3.AppendLine(ref handler);
		foreach (Finding item in findings)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(15, 3, stringBuilder2);
			handler.AppendLiteral("## ");
			handler.AppendFormatted(item.Rule);
			handler.AppendLiteral("\n\n대상 TC: ");
			handler.AppendFormatted(string.Join(", ", item.Cases));
			handler.AppendLiteral("\n\n");
			handler.AppendFormatted(item.Recommendation);
			handler.AppendLiteral("\n");
			stringBuilder4.AppendLine(ref handler);
		}
		if (findings.Count == 0)
		{
			stringBuilder.AppendLine("현재 규칙으로 감지된 개선 제안이 없습니다. QA 품질 전체의 보증은 아닙니다.");
		}
		await AtomicWrite(Path.Combine(suiteRoot, "SOP-REVIEW.md"), stringBuilder.ToString());
	}

	private static async Task AtomicWrite(string path, string content)
	{
		await File.WriteAllTextAsync(path + ".tmp", content);
		File.Move(path + ".tmp", path, overwrite: true);
	}
}
