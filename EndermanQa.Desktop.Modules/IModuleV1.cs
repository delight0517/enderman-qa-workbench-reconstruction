using System;

namespace EndermanQa.Desktop.Modules;

public interface IModuleV1 : IDisposable
{
	string ModuleId { get; }

	string Version { get; }

	string SourceHash { get; }

	object CaptureState();

	void Initialize(object? state);
}
