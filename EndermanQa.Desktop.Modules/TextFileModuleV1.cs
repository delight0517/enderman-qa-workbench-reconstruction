using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EndermanQa.Desktop.Modules;

public sealed class TextFileModuleV1 : IModuleV1, IDisposable
{
	private readonly string _path;

	private string _content = string.Empty;

	public string ModuleId => "qa.sop-review";

	public string Version => "1.0.0";

	public string SourceHash { get; private set; } = string.Empty;


	public string Content => _content;

	public TextFileModuleV1(string path)
	{
		_path = Path.GetFullPath(path);
	}

	public object CaptureState()
	{
		return new
		{
			content = _content
		};
	}

	public void Initialize(object? state)
	{
		byte[] array = File.ReadAllBytes(_path);
		if (array.Length > 1048576)
		{
			throw new InvalidDataException("Module exceeds 1 MiB.");
		}
		SourceHash = Convert.ToHexString(SHA256.HashData(array)).ToLowerInvariant();
		string @string = Encoding.UTF8.GetString(array);
		if (@string.Contains("\0"))
		{
			throw new InvalidDataException("Module file is not valid text.");
		}
		_content = @string;
	}

	public void Dispose()
	{
		_content = string.Empty;
	}
}
