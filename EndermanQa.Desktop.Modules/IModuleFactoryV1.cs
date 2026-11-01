namespace EndermanQa.Desktop.Modules;

public interface IModuleFactoryV1
{
	string ModuleId { get; }

	string Version { get; }

	string SourcePath { get; }

	IModuleV1 Create(string verifiedSourcePath);
}
