using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EndermanQa.Desktop.Adapters;

public static class GameProjectEngineDetector
{
	public static GameProjectEngineDetection Detect(string projectRoot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot, "projectRoot");
		string fullPath = Path.GetFullPath(projectRoot);
		if (!Directory.Exists(fullPath))
		{
			throw new DirectoryNotFoundException("선택한 게임 프로젝트 폴더가 없습니다.");
		}
		List<GameProjectEngineDetection> list = new List<GameProjectEngineDetection>();
		string path = Path.Combine(fullPath, "ProjectSettings", "ProjectVersion.txt");
		if (File.Exists(path))
		{
			list.Add(new GameProjectEngineDetection(Version: File.ReadLines(path).Take(8).FirstOrDefault((string line) => line.Contains("m_EditorVersion:", StringComparison.Ordinal))?.Split(':', 2).LastOrDefault()?.Trim(), EngineId: "unity", EvidenceRelativePath: Path.Combine("ProjectSettings", "ProjectVersion.txt"), Detail: "Unity ProjectSettings marker"));
		}
		if (File.Exists(Path.Combine(fullPath, "project.godot")))
		{
			list.Add(new GameProjectEngineDetection("godot", "project.godot", "Godot project marker"));
		}
		string[] array = Directory.EnumerateFiles(fullPath, "*.uproject", SearchOption.TopDirectoryOnly).Take(2).ToArray();
		if (array.Length == 1)
		{
			list.Add(new GameProjectEngineDetection("unreal", Path.GetFileName(array[0]), "Unreal project descriptor"));
		}
		else if (array.Length > 1)
		{
			list.Add(new GameProjectEngineDetection("unknown", "*.uproject", "Multiple Unreal project descriptors; select the exact project folder."));
		}
		string[] array2 = list.Select((GameProjectEngineDetection marker) => marker.EngineId).Distinct<string>(StringComparer.Ordinal).ToArray();
		if (array2.Length == 0)
		{
			return new GameProjectEngineDetection("unknown", "", "No supported engine marker found at the selected folder root.");
		}
		if (array2.Length > 1 || array2.Contains<string>("unknown", StringComparer.Ordinal))
		{
			return new GameProjectEngineDetection("unknown", string.Join("; ", list.Select((GameProjectEngineDetection marker) => marker.EvidenceRelativePath)), "Conflicting or ambiguous engine markers; choose the exact game project root.");
		}
		return list[0];
	}
}
