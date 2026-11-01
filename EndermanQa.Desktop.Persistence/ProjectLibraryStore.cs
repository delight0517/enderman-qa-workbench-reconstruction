using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EndermanQa.Desktop.Persistence;

public sealed class ProjectLibraryStore
{
	public sealed record ProjectFileEntry(string Id, string Name, string RelativePath, long SizeBytes, string Sha256, DateTimeOffset AddedAt);

	private sealed record FileIndex(int ContractVersion, IReadOnlyList<ProjectFileEntry> Files);

	public const int ContractVersion = 1;

	public const long MaximumFileBytes = 104857600L;

	private const int MaximumEntries = 1000;

	private const long MaximumTotalBytes = 2147483648L;

	private const int MaximumThumbnailBytes = 16777216;

	private static readonly HashSet<string> RegisteredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".mov", ".webm", ".avi",
		".txt", ".md", ".log", ".csv", ".json", ".xml", ".yaml", ".yml", ".toml", ".pdf",
		".docx", ".xlsx", ".pptx", ".cs", ".js", ".ts", ".html", ".css", ".py", ".java",
		".lua"
	};

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	public void InitializeProjectFolders(QaProjectDefinition project)
	{
		ValidateProject(project);
		string[] array = new string[6]
		{
			project.DataRoot,
			Path.Combine(project.RootPath, "assets"),
			Path.Combine(project.RootPath, "files"),
			Path.Combine(project.RootPath, "evidence"),
			Path.Combine(project.RootPath, "reports"),
			Path.Combine(project.RootPath, "exports")
		};
		for (int i = 0; i < array.Length; i++)
		{
			Directory.CreateDirectory(array[i]);
		}
		array = new string[7]
		{
			project.RootPath,
			project.DataRoot,
			Path.Combine(project.RootPath, "assets"),
			Path.Combine(project.RootPath, "files"),
			Path.Combine(project.RootPath, "evidence"),
			Path.Combine(project.RootPath, "reports"),
			Path.Combine(project.RootPath, "exports")
		};
		for (int i = 0; i < array.Length; i++)
		{
			RejectReparse(array[i]);
		}
		RejectReparse(Path.Combine(project.RootPath, "project.json"));
		if (!File.Exists(IndexPath(project)))
		{
			WriteIndex(project, Array.Empty<ProjectFileEntry>());
		}
		string path = Path.Combine(project.RootPath, "PROJECT-README.md");
		if (!File.Exists(path))
		{
			WriteNew(path, $"# {project.Name}\n\n- Project: `{project.ProjectId}`\n- Game/app: {project.GameTarget}\n- Description: {project.Description ?? "(not set)"}\n\n## Folders\n\n- `assets/`: project thumbnail and visual identity.\n- `files/`: copies of files registered to this project.\n- `evidence/`: screenshots and videos.\n- `reports/` and `exports/`: reviewable reports and handoff files.\n- `data/`: Workbench-managed QA records, conversations and generated files.\n\nOnly this project's data is shown when the project is active.\n");
		}
	}

	public void SetThumbnail(QaProjectDefinition project, string sourcePath, QaProjectCatalog catalog)
	{
		ValidateProject(project);
		string fullPath = Path.GetFullPath(sourcePath);
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException("선택한 썸네일 파일을 찾을 수 없습니다.", fullPath);
		}
		FileInfo fileInfo = new FileInfo(fullPath);
		long length = fileInfo.Length;
		if ((length <= 0 || length > 16777216) ? true : false)
		{
			throw new InvalidDataException("썸네일은 1 byte~16 MiB 이미지여야 합니다.");
		}
		if (!new string[5] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }.Contains<string>(fileInfo.Extension, StringComparer.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("썸네일은 PNG, JPG, BMP 또는 GIF를 선택하세요.");
		}
		InitializeProjectFolders(project);
		BitmapFrame bitmapFrame;
		using (FileStream bitmapStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			bitmapFrame = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.FirstOrDefault() ?? throw new InvalidDataException("이미지 프레임을 읽을 수 없습니다.");
		}
		int pixelWidth = bitmapFrame.PixelWidth;
		bool flag = ((pixelWidth <= 0 || pixelWidth > 4096) ? true : false);
		bool flag2 = flag;
		if (!flag2)
		{
			int pixelHeight = bitmapFrame.PixelHeight;
			bool flag3 = ((pixelHeight <= 0 || pixelHeight > 4096) ? true : false);
			flag2 = flag3;
		}
		if (flag2 || (long)bitmapFrame.PixelWidth * (long)bitmapFrame.PixelHeight > 16000000)
		{
			throw new InvalidDataException("썸네일 크기는 각 변 4096px, 전체 1600만 픽셀까지 지원합니다.");
		}
		SaveThumbnailFrame(project, bitmapFrame, catalog);
	}

	public void SetDefaultThumbnail(QaProjectDefinition project, QaProjectCatalog catalog)
	{
		ValidateProject(project);
		InitializeProjectFolders(project);
		if (!File.Exists(Path.Combine(project.RootPath, "case-catalog-100.json")))
		{
			SaveThumbnailFrame(project, CreateGenericQaThumbnail(), catalog);
			return;
		}
		using Stream bitmapStream = (Application.GetResourceStream(new Uri("pack://application:,,,/enderman-qa.png")) ?? throw new FileNotFoundException("Workbench 기본 프로젝트 썸네일 리소스를 찾을 수 없습니다.")).Stream;
		BitmapFrame frame = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.FirstOrDefault() ?? throw new InvalidDataException("기본 썸네일을 읽을 수 없습니다.");
		SaveThumbnailFrame(project, frame, catalog);
	}

	private static BitmapFrame CreateGenericQaThumbnail()
	{
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(24, 39, 64)), null, new Rect(0.0, 0.0, 640.0, 360.0), 22.0, 22.0);
			drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(44, 70, 105)), null, new Rect(32.0, 32.0, 576.0, 296.0), 18.0, 18.0);
			double pixelsPerDip = VisualTreeHelper.GetDpi(drawingVisual).PixelsPerDip;
			FormattedText formattedText = new FormattedText("QA", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 112.0, Brushes.White, pixelsPerDip);
			drawingContext.DrawText(formattedText, new Point(246.0, 66.0));
			FormattedText formattedText2 = new FormattedText("GAME TEST PROJECT", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 24.0, new SolidColorBrush(Color.FromRgb(183, 220, byte.MaxValue)), pixelsPerDip);
			drawingContext.DrawText(formattedText2, new Point(168.0, 226.0));
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(640, 360, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		return BitmapFrame.Create(renderTargetBitmap);
	}

	private static void SaveThumbnailFrame(QaProjectDefinition project, BitmapFrame frame, QaProjectCatalog catalog)
	{
		string text = Path.Combine(project.RootPath, "assets", "thumbnail.png");
		string text2 = text + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
			pngBitmapEncoder.Frames.Add(BitmapFrame.Create(frame));
			using (FileStream fileStream = new FileStream(text2, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				pngBitmapEncoder.Save(fileStream);
				fileStream.Flush(flushToDisk: true);
			}
			File.Move(text2, text, overwrite: true);
			catalog.UpdateProjectPresentation(project.ProjectId, null, null, Path.Combine("assets", "thumbnail.png"));
		}
		catch
		{
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
			throw;
		}
	}

	public BitmapFrame? LoadThumbnail(QaProjectDefinition project, string fallbackResourcePath)
	{
		ValidateProject(project);
		if (!string.IsNullOrWhiteSpace(project.ThumbnailRelativePath))
		{
			string text = ResolveInside(project.RootPath, project.ThumbnailRelativePath);
			if (File.Exists(text) && new FileInfo(text).Length <= 16777216)
			{
				RejectReparse(text);
				try
				{
					using FileStream bitmapStream = new FileStream(text, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
					return BitmapFrame.Create(bitmapStream, BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
				}
				catch (Exception ex) when (((ex is IOException || ex is NotSupportedException || ex is InvalidOperationException || ex is FormatException) ? 1 : 0) != 0)
				{
				}
			}
		}
		if (!File.Exists(fallbackResourcePath))
		{
			return null;
		}
		try
		{
			using FileStream bitmapStream2 = File.OpenRead(fallbackResourcePath);
			return BitmapFrame.Create(bitmapStream2, BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is NotSupportedException || ex2 is InvalidOperationException || ex2 is FormatException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	public IReadOnlyList<ProjectFileEntry> ListFiles(QaProjectDefinition project)
	{
		QaProjectDefinition project2 = project;
		ValidateProject(project2);
		return ReadIndex(project2).Files.Where((ProjectFileEntry entry) => IsRegisteredFileValid(project2, entry)).OrderBy<ProjectFileEntry, string>((ProjectFileEntry x) => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
	}

	public IReadOnlyList<ProjectFileEntry> RegisterFiles(QaProjectDefinition project, IEnumerable<string> sourcePaths)
	{
		ValidateProject(project);
		InitializeProjectFolders(project);
		List<ProjectFileEntry> list = ReadIndex(project).Files.ToList();
		List<string> list2 = new List<string>();
		List<ProjectFileEntry> list3 = new List<ProjectFileEntry>();
		try
		{
			foreach (string item2 in sourcePaths.Distinct<string>(StringComparer.OrdinalIgnoreCase))
			{
				string fullPath = Path.GetFullPath(item2);
				FileInfo fileInfo = new FileInfo(fullPath);
				if (!fileInfo.Exists)
				{
					throw new FileNotFoundException("등록할 파일을 찾을 수 없습니다.", fullPath);
				}
				if (!RegisteredExtensions.Contains(fileInfo.Extension))
				{
					throw new InvalidDataException("등록할 수 없는 파일 형식입니다: " + fileInfo.Extension);
				}
				long length = fileInfo.Length;
				if ((length <= 0 || length > 104857600) ? true : false)
				{
					throw new InvalidDataException($"파일은 1 byte~{100} MiB만 등록할 수 있습니다: {fileInfo.Name}");
				}
				RejectReparse(fullPath);
				if (list.Sum((ProjectFileEntry x) => x.SizeBytes) + fileInfo.Length > 2147483648u)
				{
					throw new InvalidDataException("프로젝트 등록 파일 전체 크기 상한 2 GiB를 넘습니다.");
				}
				string hash = HashFile(fullPath);
				ProjectFileEntry projectFileEntry = list.FirstOrDefault((ProjectFileEntry x) => x.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase));
				if ((object)projectFileEntry != null)
				{
					list3.Add(projectFileEntry);
					continue;
				}
				if (list.Count >= 1000)
				{
					throw new InvalidDataException($"프로젝트당 등록 파일은 최대 {1000}개입니다.");
				}
				string text = Guid.NewGuid().ToString("N");
				string path = text + "-" + SafeFileName(fileInfo.Name);
				string text2 = Path.Combine("files", path);
				string text3 = ResolveInside(project.RootPath, text2);
				string text4 = text3 + ".tmp";
				using (FileStream fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					using FileStream fileStream2 = new FileStream(text4, FileMode.CreateNew, FileAccess.Write, FileShare.None);
					fileStream.CopyTo(fileStream2);
					fileStream2.Flush(flushToDisk: true);
				}
				File.Move(text4, text3);
				list2.Add(text3);
				ProjectFileEntry item = new ProjectFileEntry(text, fileInfo.Name, text2, fileInfo.Length, hash, DateTimeOffset.UtcNow);
				list.Add(item);
				list3.Add(item);
			}
			WriteIndex(project, list);
			return list3;
		}
		catch
		{
			foreach (string item3 in list2)
			{
				if (File.Exists(item3))
				{
					File.Delete(item3);
				}
			}
			throw;
		}
	}

	public string GetFilePath(QaProjectDefinition project, ProjectFileEntry entry)
	{
		ValidateProject(project);
		string text = ResolveInside(project.RootPath, entry.RelativePath);
		RejectReparse(text);
		FileInfo fileInfo = new FileInfo(text);
		if (!fileInfo.Exists || fileInfo.Length != entry.SizeBytes || !HashFile(text).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("프로젝트 파일이 없거나 등록 이후 바뀌었습니다. 파일 목록을 새로 고쳐주세요.");
		}
		return text;
	}

	public void OpenFolder(QaProjectDefinition project, string folder)
	{
		ValidateProject(project);
		InitializeProjectFolders(project);
		HashSet<string> obj = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			project.RootPath,
			project.DataRoot,
			Path.Combine(project.RootPath, "files"),
			Path.Combine(project.RootPath, "evidence"),
			Path.Combine(project.RootPath, "reports"),
			Path.Combine(project.RootPath, "exports")
		};
		string fullPath = Path.GetFullPath(folder);
		if (!obj.Contains(fullPath) || !Directory.Exists(fullPath))
		{
			throw new InvalidOperationException("선택한 폴더가 이 프로젝트의 허용된 작업 공간이 아닙니다.");
		}
		RejectPathChain(project.RootPath, fullPath);
		Process.Start(new ProcessStartInfo(fullPath)
		{
			UseShellExecute = true
		});
	}

	public void OpenFile(QaProjectDefinition project, ProjectFileEntry entry)
	{
		Process.Start(new ProcessStartInfo(GetFilePath(project, entry))
		{
			UseShellExecute = true
		});
	}

	private static FileIndex ReadIndex(QaProjectDefinition project)
	{
		string text = IndexPath(project);
		if (!File.Exists(text))
		{
			return new FileIndex(1, Array.Empty<ProjectFileEntry>());
		}
		RejectReparse(text);
		if (new FileInfo(text).Length > 2097152)
		{
			throw new InvalidDataException("프로젝트 파일 목록이 2 MiB 상한을 넘습니다.");
		}
		FileIndex fileIndex = JsonSerializer.Deserialize<FileIndex>(File.ReadAllText(text), JsonOptions) ?? throw new InvalidDataException("프로젝트 파일 목록이 손상되었습니다.");
		if (fileIndex.ContractVersion != 1 || fileIndex.Files.Count > 1000)
		{
			throw new InvalidDataException("프로젝트 파일 목록 버전/크기가 지원되지 않습니다.");
		}
		foreach (ProjectFileEntry file in fileIndex.Files)
		{
			if (!IsFilesRelativePath(file.RelativePath))
			{
				throw new InvalidDataException("등록 파일 경로는 프로젝트 files 폴더 안에 있어야 합니다.");
			}
			ResolveInside(project.RootPath, file.RelativePath);
		}
		return fileIndex;
	}

	private static void WriteIndex(QaProjectDefinition project, IReadOnlyList<ProjectFileEntry> files)
	{
		string text = IndexPath(project);
		Directory.CreateDirectory(Path.GetDirectoryName(text));
		string text2 = text + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using FileStream fileStream = new FileStream(text2, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			JsonSerializer.Serialize(fileStream, new FileIndex(1, files), JsonOptions);
			fileStream.Flush(flushToDisk: true);
			fileStream.Close();
			File.Move(text2, text, overwrite: true);
		}
		catch
		{
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
			throw;
		}
	}

	private static bool IsRegisteredFileValid(QaProjectDefinition project, ProjectFileEntry entry)
	{
		try
		{
			if (!IsFilesRelativePath(entry.RelativePath))
			{
				return false;
			}
			string text = ResolveInside(project.RootPath, entry.RelativePath);
			RejectPathChain(project.RootPath, text);
			return File.Exists(text) && new FileInfo(text).Length == entry.SizeBytes;
		}
		catch (Exception ex) when (((ex is IOException || ex is InvalidDataException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			return false;
		}
	}

	private static string IndexPath(QaProjectDefinition project)
	{
		return Path.Combine(project.RootPath, "files", "registry-v1.json");
	}

	private static bool IsFilesRelativePath(string relative)
	{
		ReadOnlySpan<char> readOnlySpan = "files";
		char reference = Path.DirectorySeparatorChar;
		if (!relative.StartsWith(string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference)), StringComparison.OrdinalIgnoreCase))
		{
			ReadOnlySpan<char> readOnlySpan2 = "files";
			reference = Path.AltDirectorySeparatorChar;
			return relative.StartsWith(string.Concat(readOnlySpan2, new ReadOnlySpan<char>(ref reference)), StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string SafeFileName(string name)
	{
		return Regex.Replace(Path.GetFileName(name), "[^\\p{L}\\p{Nd}._() -]+", "_").Trim('.', ' ');
	}

	private static string HashFile(string path)
	{
		using FileStream source = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(source));
	}

	private static string ResolveInside(string root, string relative)
	{
		if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any((string x) => (x == ".." || x == ".") ? true : false))
		{
			throw new InvalidDataException("프로젝트 파일 경로가 올바르지 않습니다.");
		}
		string fullPath = Path.GetFullPath(Path.Combine(root, relative));
		ReadOnlySpan<char> readOnlySpan = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
		char reference = Path.DirectorySeparatorChar;
		string value = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("프로젝트 파일이 프로젝트 폴더 밖을 가리킵니다.");
		}
		RejectPathChain(root, fullPath);
		return fullPath;
	}

	private static void ValidateProject(QaProjectDefinition project)
	{
		string fullPath = Path.GetFullPath(project.RootPath);
		string fullPath2 = Path.GetFullPath(project.DataRoot);
		if (!File.Exists(Path.Combine(fullPath, "project.json")) || !fullPath2.Equals(Path.Combine(fullPath, "data"), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("프로젝트 metadata와 data 경계가 일치하지 않습니다.");
		}
	}

	private static void RejectReparse(string path)
	{
		if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
		{
			throw new IOException("reparse point 파일은 프로젝트 라이브러리에서 사용하지 않습니다.");
		}
	}

	private static void RejectPathChain(string root, string path)
	{
		string fullPath = Path.GetFullPath(root);
		string fullPath2 = Path.GetFullPath(path);
		ReadOnlySpan<char> readOnlySpan = fullPath.TrimEnd(Path.DirectorySeparatorChar);
		char reference = Path.DirectorySeparatorChar;
		string text = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		if (!fullPath2.Equals(fullPath, StringComparison.OrdinalIgnoreCase) && !fullPath2.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("프로젝트 경로가 root 경계를 벗어납니다.");
		}
		string text3;
		if (!fullPath2.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
		{
			string text2 = fullPath2;
			int length = text.Length;
			text3 = text2.Substring(length, text2.Length - length);
		}
		else
		{
			text3 = string.Empty;
		}
		string text4 = fullPath;
		if (Directory.Exists(text4) || File.Exists(text4))
		{
			RejectReparse(text4);
		}
		string[] array = text3.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
		foreach (string path2 in array)
		{
			text4 = Path.Combine(text4, path2);
			if (Directory.Exists(text4) || File.Exists(text4))
			{
				RejectReparse(text4);
			}
		}
	}

	private static void WriteNew(string path, string text)
	{
		using FileStream fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		using StreamWriter streamWriter = new StreamWriter(fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.Write(text);
		streamWriter.Flush();
		fileStream.Flush(flushToDisk: true);
	}
}
