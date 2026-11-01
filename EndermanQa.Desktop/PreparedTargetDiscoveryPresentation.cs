using System;
using EndermanQa.Desktop.Adapters;

namespace EndermanQa.Desktop;

internal static class PreparedTargetDiscoveryPresentation
{
	public static string NoCandidates => "프로젝트 연결 대상 기록 없음 · 재개 가능한 앱 소유 준비 인스턴스를 찾지 못했습니다. 새 격리 인스턴스를 준비하거나 기존 ID를 확인하세요.";

	public static string MultipleCandidates(int count)
	{
		if (count < 2) throw new ArgumentOutOfRangeException(nameof(count));
		return $"프로젝트 연결 기록 없음 · 재개 가능한 준비 인스턴스 {count}개 발견 · 실행 기록의 ID 목록에서 사용할 대상을 복사해 입력하세요.";
	}

	public static string CandidateStatus(JavaServerWorkspace candidate, bool eulaAccepted)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		return $"재개 준비 인스턴스 발견 · ID {candidate.InstanceId} · EULA {(eulaAccepted ? "동의됨" : "미동의")} · 월드 없음 · 서버/클라이언트 미실행 · ID 확인 후 기존 인스턴스를 불러오세요.";
	}

	public static string CandidateLogEntry(JavaServerWorkspace candidate, bool eulaAccepted)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		return $"재개 준비 인스턴스 발견 · ID {candidate.InstanceId} · EULA {(eulaAccepted ? "동의됨" : "미동의")} · 검증 시 월드 없음 · 검색은 서버/클라이언트를 시작하지 않음. ID 확인 후 기존 인스턴스를 불러오세요.";
	}
}
