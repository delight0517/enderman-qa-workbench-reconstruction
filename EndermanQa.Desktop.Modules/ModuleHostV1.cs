using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace EndermanQa.Desktop.Modules;

public sealed class ModuleHostV1<T> : IDisposable where T : class, IModuleV1
{
	private T? _active;

	public T? Active => _active;

	public string? LastError { get; private set; }

	public bool Apply(IModuleFactoryV1 factory, string expectedId, string expectedVersion, string expectedHash)
	{
		if (factory.ModuleId != expectedId || factory.Version != expectedVersion || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
		{
			return Reject("Candidate contract or hash is invalid.");
		}
		T val = null;
		object obj = null;
		string fullPath = Path.GetFullPath(factory.SourcePath);
		try
		{
			if (!File.Exists(fullPath))
			{
				throw new FileNotFoundException("Candidate module source is missing.", fullPath);
			}
			if (!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant().Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("Candidate source hash changed after review.");
			}
			val = (factory.Create(fullPath) as T) ?? throw new InvalidOperationException("Module type mismatch.");
			if (val.ModuleId != expectedId || val.Version != expectedVersion)
			{
				throw new InvalidDataException("Candidate module contract does not match the reviewed identity.");
			}
			obj = _active?.CaptureState();
			val.Initialize(obj);
			if (!val.SourceHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("Initialized module hash does not match the reviewed candidate.");
			}
			T active = _active;
			_active = val;
			val = null;
			try
			{
				active?.Dispose();
			}
			catch (Exception ex)
			{
				LastError = "New module active; previous module cleanup failed: " + ex.Message;
			}
			LastError = null;
			return true;
		}
		catch (Exception ex2)
		{
			LastError = ex2.Message;
			return false;
		}
		finally
		{
			val?.Dispose();
		}
	}

	public bool Restore(IModuleFactoryV1 factory, string id, string version, string hash)
	{
		return Apply(factory, id, version, hash);
	}

	public void Dispose()
	{
		_active?.Dispose();
		_active = null;
	}

	private bool Reject(string error)
	{
		LastError = error;
		return false;
	}
}
