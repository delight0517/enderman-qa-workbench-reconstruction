using System.IO;
using System.Runtime.CompilerServices;

namespace EndermanQa.Desktop.Modules;

public sealed class TextFileModuleFactoryV1 : IModuleFactoryV1
{
	[CompilerGenerated]
	private string _003Cversion_003EP;

	public string ModuleId => "qa.sop-review";

	public string Version => _003Cversion_003EP;

	public string SourcePath { get; }

	public TextFileModuleFactoryV1(string path, string version)
	{
		_003Cversion_003EP = version;
		SourcePath = Path.GetFullPath(path);
		
	}

	public IModuleV1 Create(string verifiedSourcePath)
	{
		return new TextFileModuleV1(verifiedSourcePath);
	}
}
