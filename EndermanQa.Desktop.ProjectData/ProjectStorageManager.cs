using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.FileIO;

namespace EndermanQa.Desktop.ProjectData;

public sealed class ProjectStorageManager
{
	private static readonly (string Id, string Name, string Relative)[] Deletable = new(string, string, string)[11]
	{
		("evidence", "스크린샷·영상 증거", "evidence"),
		("reports", "프로젝트 보고서", "reports"),
		("exports", "내보낸 제출 파일", "exports"),
		("qa-runs", "QA 실행 로그", "data/runs"),
		("suite-runs", "100건·선택 케이스 실행 기록", "data/suite-runs"),
		("manual-cases", "수동 QA 기록", "data/manual-cases"),
		("attachments", "첨부 파일", "data/attachments"),
		("generated", "AI 생성 산출물", "data/generated"),
		("tool-workspaces", "도구 제작 산출물", "data/tool-workspaces"),
		("onlychat-runs", "Only Chat 작업 영수증", "data/onlychat-runs"),
		("ai-chats", "AI 대화 기록", "data/ai-chat-tool-builder")
	};

	

	private readonly string _root;

	public ProjectStorageManager(string projectRoot)
	{
		_root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar);
	}

	public ProjectStorageReport Measure()
	{
		EnsureDirectoryIsNotReparse(_root);
		List<string> warnings = new List<string>();
		(long Bytes, int Files) tuple = MeasureTree(_root, warnings);
		long item2 = tuple.Bytes;
		int item3 = tuple.Files;
		ProjectStorageCategory[] categories = Deletable.Select(delegate((string Id, string Name, string Relative) item)
		{
			string text = ResolveAllowed(item.Relative);
			if (!Directory.Exists(text))
			{
				return new ProjectStorageCategory(item.Id, item.Name, item.Relative, 0, 0L, Exists: false);
			}
			var (sizeBytes, fileCount) = MeasureTree(text, warnings);
			return new ProjectStorageCategory(item.Id, item.Name, item.Relative, fileCount, sizeBytes, Exists: true);
		}).ToArray();
		return new ProjectStorageReport(_root, item2, item3, warnings.Count == 0, categories, warnings.Distinct().Take(20).ToArray());
	}

	public void MoveToRecycleBin(string categoryId)
	{
		string categoryId2 = categoryId;
		(string, string, string) tuple = Deletable.SingleOrDefault(((string Id, string Name, string Relative) x) => x.Id.Equals(categoryId2, StringComparison.Ordinal));
		(string, string, string) tuple2 = tuple;
		if (tuple2.Item1 == null && tuple2.Item2 == null && tuple2.Item3 == null)
		{
			throw new InvalidDataException("선택한 저장공간 범주는 삭제 허용 목록에 없습니다.");
		}
		string text = ResolveAllowed(tuple.Item3);
		if (Directory.Exists(text))
		{
			EnsurePathChainIsNotReparse(text);
			FileSystem.DeleteDirectory(text, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
		}
	}

	private string ResolveAllowed(string relative)
	{
		if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(delegate(string x)
		{
			switch (x)
			{
			default:
				if (x.Length != 0)
				{
					break;
				}
				goto case "..";
			case "..":
			case ".":
				return true;
			case null:
				break;
			}
			return false;
		}))
		{
			throw new InvalidDataException("내부 저장공간 범주 경로가 안전한 상대경로가 아닙니다.");
		}
		string fullPath = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
		ReadOnlySpan<char> readOnlySpan = _root;
		char reference = Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference)), StringComparison.OrdinalIgnoreCase))
		{
			throw new UnauthorizedAccessException("삭제 경로가 현재 프로젝트 바깥으로 나갔습니다.");
		}
		return fullPath;
	}

	private void EnsurePathChainIsNotReparse(string target)
	{
		EnsureDirectoryIsNotReparse(_root);
		string relativePath = Path.GetRelativePath(_root, target);
		string text = _root;
		string[] array = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		foreach (string path in array)
		{
			text = Path.Combine(text, path);
			if (Directory.Exists(text))
			{
				EnsureDirectoryIsNotReparse(text);
				continue;
			}
			break;
		}
	}

	private static (long Bytes, int Files) MeasureTree(string start, List<string> warnings)
	{
		long num = 0L;
		int num2 = 0;
		Stack<string> stack = new Stack<string>();
		stack.Push(start);
		while (stack.Count > 0)
		{
			string path = stack.Pop();
			try
			{
				if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
				{
					continue;
				}
				foreach (string item in Directory.EnumerateFileSystemEntries(path))
				{
					try
					{
						FileAttributes attributes = File.GetAttributes(item);
						if ((attributes & FileAttributes.ReparsePoint) == 0)
						{
							if ((attributes & FileAttributes.Directory) != 0)
							{
								stack.Push(item);
								continue;
							}
							num = checked(num + new FileInfo(item).Length);
							num2++;
						}
					}
					catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
					{
						warnings.Add("접근할 수 없는 항목: " + Path.GetRelativePath(start, item));
					}
				}
			}
			catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
				warnings.Add("접근할 수 없는 폴더: " + Path.GetRelativePath(start, path));
			}
		}
		return (Bytes: num, Files: num2);
	}

	private static void EnsureDirectoryIsNotReparse(string path)
	{
		if (!Directory.Exists(path))
		{
			throw new DirectoryNotFoundException("프로젝트 저장 폴더가 없습니다.");
		}
		if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
		{
			throw new UnauthorizedAccessException("프로젝트 저장소의 재분석 지점을 통과할 수 없습니다.");
		}
	}
}
