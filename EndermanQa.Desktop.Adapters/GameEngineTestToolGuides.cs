namespace EndermanQa.Desktop.Adapters;

public static class GameEngineTestToolGuides
{
	public static GameEngineTestToolGuide For(string? engineId)
	{
		return (engineId ?? string.Empty).Trim().ToLowerInvariant() switch
		{
			"unity" => new GameEngineTestToolGuide("unity", "Unity Test Framework", "Edit Mode 및 Play Mode 테스트; 프로젝트 소유 테스트 어셈블리가 필요합니다.", "Workbench 매크로 어댑터와는 별도 도구입니다. 실제 게임 자동화는 대상/오라클/입력 브리지가 검증돼야 합니다.", "https://docs.unity3d.com/Manual/com.unity.test-framework.html"), 
			"unreal" => new GameEngineTestToolGuide("unreal", "Unreal Automation Framework", "엔진의 unit, feature, functional 및 content 테스트를 실행합니다.", "프로젝트별 테스트 플러그인/세션이 필요할 수 있습니다. Workbench 연결 어댑터는 별도입니다.", "https://dev.epicgames.com/documentation/en-us/unreal-engine/automation-test-framework-in-unreal-engine"), 
			"godot" => new GameEngineTestToolGuide("godot", "Godot CLI / headless project runner", "프로젝트 스크립트와 headless 검증에 적합합니다.", "headless 실행은 시각 플레이 검증이 아닙니다. 화면·입력 검증에는 별도 소유 클라이언트 브리지가 필요합니다.", "https://docs.godotengine.org/en/latest/tutorials/editor/command_line_tutorial.html"), 
			"minecraft-java" => new GameEngineTestToolGuide("minecraft-java", "Minecraft Java · Enderman suite", "현재 Java 경로는 Enderman 전용 시나리오용입니다.", "범용 Minecraft/타 게임 매크로 어댑터가 아닙니다. 대상은 별도 격리 정책을 통과해야 합니다.", string.Empty), 
			"custom" => new GameEngineTestToolGuide("custom", "게임/엔진별 조사 필요", "엔진을 확인한 뒤 게임이 공식 제공하는 테스트 도구나 계측 인터페이스를 선택합니다.", "도구를 연결하기 전까지 매크로는 저장/미리보기만 가능합니다.", string.Empty), 
			_ => new GameEngineTestToolGuide("unknown", "엔진을 먼저 확인하세요", "소스 폴더의 엔진 표식을 읽기 전용으로 감지하거나 수동 설정합니다.", "호환되는 실제 어댑터가 없으면 게임 입력/테스트를 시작하지 않습니다.", string.Empty), 
		};
	}
}
