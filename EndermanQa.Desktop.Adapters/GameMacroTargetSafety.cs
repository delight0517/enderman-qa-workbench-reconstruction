using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EndermanQa.Desktop.Scenarios;

namespace EndermanQa.Desktop.Adapters;

public static class GameMacroTargetSafety
{
	private static readonly TimeSpan ProcessStartTolerance = TimeSpan.FromSeconds(2.0);

	public static bool IsVerified(GameTargetVerification? verification, GameToolDescriptor descriptor, GameAutomationTarget target)
	{
		if ((object)verification == null || !verification.IsVerified || !verification.IsTaskOwned || !string.Equals(verification.GameId, target.GameId, StringComparison.Ordinal) || !string.Equals(verification.TargetId, target.TargetId, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(verification.EvidenceKind) || !Regex.IsMatch(verification.EvidenceSha256 ?? string.Empty, "^[A-Fa-f0-9]{64}$") || verification.VerifiedAt > DateTimeOffset.UtcNow.AddMinutes(1.0) || verification.VerifiedAt < DateTimeOffset.UtcNow.AddMinutes(-5.0))
		{
			return false;
		}
		if (descriptor.RequiresOwnedProcess && !HasValidProcessAttestation(verification, target))
		{
			return false;
		}
		if (descriptor.RequiresUnmirroredDisplay && !verification.IsUnmirrored)
		{
			return false;
		}
		return true;
	}

	public static string ComputeProcessAttestationHash(string gameId, string targetId, int processId, string processImagePath, string processImageSha256, DateTimeOffset processStartedAtUtc)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(gameId, "gameId");
		ArgumentException.ThrowIfNullOrWhiteSpace(targetId, "targetId");
		if (processId <= 0)
		{
			throw new ArgumentOutOfRangeException("processId");
		}
		if (!IsSha256(processImageSha256))
		{
			throw new ArgumentException("Executable SHA-256 must be 64 hexadecimal characters.", "processImageSha256");
		}
		string fullPath = Path.GetFullPath(processImagePath);
		string s = JsonSerializer.Serialize(new
		{
			Version = 1,
			GameId = gameId,
			TargetId = targetId,
			ProcessId = processId,
			ProcessImagePath = (OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath),
			ProcessImageSha256 = processImageSha256.ToUpperInvariant(),
			ProcessStartedAtUtc = processStartedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
		});
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
	}

	public static bool IsLiveProcessStillBound(GameTargetVerification? verification, GameToolDescriptor descriptor, GameAutomationTarget target)
	{
		if (!descriptor.RequiresOwnedProcess)
		{
			return true;
		}
		bool flag = (object)verification == null || !verification.IsVerified || !verification.IsTaskOwned;
		if (!flag)
		{
			int? processId = verification.ProcessId;
			bool flag2 = ((!processId.HasValue || processId.GetValueOrDefault() <= 0) ? true : false);
			flag = flag2;
		}
		if (flag || string.IsNullOrWhiteSpace(verification.ProcessImagePath) || !verification.ProcessStartedAtUtc.HasValue)
		{
			return false;
		}
		try
		{
			using Process process = Process.GetProcessById(verification.ProcessId.Value);
			if (process.HasExited)
			{
				flag = false;
			}
			else
			{
				string text = process.MainModule?.FileName;
				flag = !string.IsNullOrWhiteSpace(text) && SamePath(text, verification.ProcessImagePath) && !(Math.Abs((new DateTimeOffset(process.StartTime.ToUniversalTime()) - verification.ProcessStartedAtUtc.Value.ToUniversalTime()).TotalSeconds) > ProcessStartTolerance.TotalSeconds) && !process.HasExited && string.Equals(verification.GameId, target.GameId, StringComparison.Ordinal) && string.Equals(verification.TargetId, target.TargetId, StringComparison.Ordinal);
			}
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is InvalidOperationException || ex is Win32Exception || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException) ? 1 : 0) != 0)
		{
			flag = false;
		}
		return flag;
	}

	private static bool HasValidProcessAttestation(GameTargetVerification verification, GameAutomationTarget target)
	{
		int? processId = verification.ProcessId;
		bool flag = ((!processId.HasValue || processId.GetValueOrDefault() <= 0) ? true : false);
		if (flag || !verification.ProcessStartedAtUtc.HasValue || string.IsNullOrWhiteSpace(verification.ProcessImagePath) || !IsSha256(verification.ProcessImageSha256))
		{
			return false;
		}
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				return false;
			}
			string fullPath = Path.GetFullPath(verification.ProcessImagePath);
			if (!File.Exists(fullPath) || !IsLiveProcessStillBound(verification, new GameToolDescriptor("process.attestation", 1, target.GameId, "process attestation", "process", "ready", RequiresOwnedProcess: true, RequiresUnmirroredDisplay: false, new HashSet<string>(new _003C_003Ez__ReadOnlySingleElementList<string>("process.verify"), StringComparer.Ordinal), new HashSet<string>(new _003C_003Ez__ReadOnlySingleElementList<string>("process.identity"), StringComparer.Ordinal)), target))
			{
				return false;
			}
			string text = HashFile(fullPath);
			if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(text), Convert.FromHexString(verification.ProcessImageSha256)))
			{
				return false;
			}
			return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(ComputeProcessAttestationHash(target.GameId, target.TargetId, verification.ProcessId.Value, fullPath, text, verification.ProcessStartedAtUtc.Value)), Convert.FromHexString(verification.EvidenceSha256));
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException || ex is NotSupportedException) ? 1 : 0) != 0)
		{
			return false;
		}
	}

	private static bool SamePath(string left, string right)
	{
		string fullPath = Path.GetFullPath(left);
		string fullPath2 = Path.GetFullPath(right);
		return string.Equals(fullPath, fullPath2, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
	}

	private static bool IsSha256(string? value)
	{
		if (value != null && value.Length == 64)
		{
			return value.All(Uri.IsHexDigit);
		}
		return false;
	}

	private static string HashFile(string path)
	{
		using FileStream source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.SequentialScan);
		return Convert.ToHexString(SHA256.HashData(source));
	}
}
