using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Persistence;

public sealed class RunJournal
{
	[CompilerGenerated]
	private string _003CfilePath_003EP;

	private readonly SemaphoreSlim _gate;

	private readonly JsonSerializerOptions _options;

	public RunJournal(string filePath)
	{
		_003CfilePath_003EP = filePath;
		_gate = new SemaphoreSlim(1, 1);
		_options = new JsonSerializerOptions
		{
			WriteIndented = false
		};
		
	}

	public async Task AppendAsync<T>(T item, CancellationToken cancellationToken = default(CancellationToken))
	{
		string line = JsonSerializer.Serialize(item, _options);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_003CfilePath_003EP));
			await File.AppendAllTextAsync(_003CfilePath_003EP, line + Environment.NewLine, cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}
}
