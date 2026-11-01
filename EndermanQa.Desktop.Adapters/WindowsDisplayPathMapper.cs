using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace EndermanQa.Desktop.Adapters;

internal static class WindowsDisplayPathMapper
{
	internal sealed record ActiveOutput(string DevicePath, string GdiDeviceName, int Left, int Top, int Right, int Bottom)
	{
		internal int Width => checked(Right - Left);

		internal int Height => checked(Bottom - Top);

		[CompilerGenerated]
		private bool PrintMembers(StringBuilder builder)
		{
			RuntimeHelpers.EnsureSufficientExecutionStack();
			builder.Append("DevicePath = ");
			builder.Append((object?)DevicePath);
			builder.Append(", GdiDeviceName = ");
			builder.Append((object?)GdiDeviceName);
			builder.Append(", Left = ");
			builder.Append(Left.ToString());
			builder.Append(", Top = ");
			builder.Append(Top.ToString());
			builder.Append(", Right = ");
			builder.Append(Right.ToString());
			builder.Append(", Bottom = ");
			builder.Append(Bottom.ToString());
			return true;
		}
	}

	private struct Luid
	{
		internal uint LowPart;

		internal int HighPart;
	}

	private struct Rational
	{
		internal uint Numerator;

		internal uint Denominator;
	}

	private struct DisplayConfigPathSourceInfo
	{
		internal Luid AdapterId;

		internal uint Id;

		internal uint ModeInfoIdx;

		internal uint StatusFlags;
	}

	private struct DisplayConfigPathTargetInfo
	{
		internal Luid AdapterId;

		internal uint Id;

		internal uint ModeInfoIdx;

		internal uint OutputTechnology;

		internal uint Rotation;

		internal uint Scaling;

		internal Rational RefreshRate;

		internal uint ScanLineOrdering;

		internal int TargetAvailable;

		internal uint StatusFlags;
	}

	private struct DisplayConfigPathInfo
	{
		internal DisplayConfigPathSourceInfo SourceInfo;

		internal DisplayConfigPathTargetInfo TargetInfo;

		internal uint Flags;
	}

	[StructLayout(LayoutKind.Explicit, Size = 48)]
	private struct DisplayConfigModeUnion
	{
		[FieldOffset(0)]
		private ulong _part0;

		[FieldOffset(8)]
		private ulong _part1;

		[FieldOffset(16)]
		private ulong _part2;

		[FieldOffset(24)]
		private ulong _part3;

		[FieldOffset(32)]
		private ulong _part4;

		[FieldOffset(40)]
		private ulong _part5;
	}

	private struct DisplayConfigModeInfo
	{
		internal uint InfoType;

		internal uint Id;

		internal Luid AdapterId;

		internal DisplayConfigModeUnion Mode;
	}

	private struct DisplayConfigDeviceInfoHeader
	{
		internal int Type;

		internal int Size;

		internal Luid AdapterId;

		internal uint Id;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DisplayConfigTargetDeviceName
	{
		internal DisplayConfigDeviceInfoHeader Header;

		internal uint Flags;

		internal uint OutputTechnology;

		internal ushort EdidManufactureId;

		internal ushort EdidProductCodeId;

		internal uint ConnectorInstance;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
		internal string MonitorFriendlyDeviceName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		internal string MonitorDevicePath;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DisplayConfigSourceDeviceName
	{
		internal DisplayConfigDeviceInfoHeader Header;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		internal string ViewGdiDeviceName;
	}

	private struct Rect
	{
		internal int Left;

		internal int Top;

		internal int Right;

		internal int Bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct MonitorInfoEx
	{
		internal int Size;

		internal Rect Monitor;

		internal Rect Work;

		internal uint Flags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		internal string DeviceName;
	}

	private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

	private const uint QdcOnlyActivePaths = 2u;

	private const uint QdcVirtualModeAware = 16u;

	private const int ErrorInsufficientBuffer = 122;

	private const int DeviceInfoGetSourceName = 1;

	private const int DeviceInfoGetTargetName = 2;

	internal static IReadOnlyList<ActiveOutput> EnumerateActiveOutputs()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Display path mapping is Windows-only.");
		}
		if (Marshal.SizeOf<DisplayConfigPathInfo>() != 72 || Marshal.SizeOf<DisplayConfigModeInfo>() != 64)
		{
			throw new InvalidOperationException("Windows display structure layout does not match the expected ABI.");
		}
		uint flags = 18u;
		for (int i = 0; i < 3; i++)
		{
			int displayConfigBufferSizes = GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
			if (displayConfigBufferSizes != 0)
			{
				throw new Win32Exception(displayConfigBufferSizes, "Could not read active Windows display configuration sizes.");
			}
			if (pathCount > 64 || modeCount > 256)
			{
				throw new InvalidOperationException("Active Windows display configuration exceeds the bounded enumeration capacity.");
			}
			checked
			{
				DisplayConfigPathInfo[] array = new DisplayConfigPathInfo[(int)pathCount];
				nint num = Marshal.AllocHGlobal((int)Math.Max(1u, modeCount) * Marshal.SizeOf<DisplayConfigModeInfo>());
				try
				{
					displayConfigBufferSizes = QueryDisplayConfig(flags, ref pathCount, array, ref modeCount, num, IntPtr.Zero);
					switch (displayConfigBufferSizes)
					{
					case 122:
						break;
					default:
						throw new Win32Exception(displayConfigBufferSizes, "Could not read active Windows display paths.");
					case 0:
						return MapPaths(array.Take((int)pathCount).ToArray(), ReadMonitorBounds());
					}
				}
				finally
				{
					Marshal.FreeHGlobal(num);
				}
			}
		}
		throw new InvalidOperationException("Windows display topology changed repeatedly during read-only enumeration.");
	}

	internal static ActiveOutput ResolveUniqueLeasedPath(string leasedDevicePath, IEnumerable<ActiveOutput> activeOutputs)
	{
		string leasedDevicePath2 = leasedDevicePath;
		if (string.IsNullOrWhiteSpace(leasedDevicePath2))
		{
			throw new ArgumentException("Leased device path is empty.", "leasedDevicePath");
		}
		ActiveOutput[] array = activeOutputs.Where((ActiveOutput output) => output.DevicePath.Equals(leasedDevicePath2, StringComparison.OrdinalIgnoreCase)).ToArray();
		if (array.Length != 1)
		{
			throw new InvalidOperationException((array.Length == 0) ? "The VDM lease path is not one of the current active display paths." : "The VDM lease path maps to multiple active outputs; placement must fail closed.");
		}
		ActiveOutput activeOutput = array[0];
		if (string.IsNullOrWhiteSpace(activeOutput.GdiDeviceName) || activeOutput.Width <= 0 || activeOutput.Height <= 0)
		{
			throw new InvalidOperationException("The leased display path has no valid current monitor bounds.");
		}
		return activeOutput;
	}

	private static IReadOnlyList<ActiveOutput> MapPaths(DisplayConfigPathInfo[] paths, IReadOnlyDictionary<string, Rect> monitorBounds)
	{
		List<ActiveOutput> list = new List<ActiveOutput>(paths.Length);
		for (int i = 0; i < paths.Length; i++)
		{
			DisplayConfigPathInfo displayConfigPathInfo = paths[i];
			if (displayConfigPathInfo.TargetInfo.TargetAvailable != 0)
			{
				DisplayConfigTargetDeviceName targetDeviceName = GetTargetDeviceName(displayConfigPathInfo.TargetInfo.AdapterId, displayConfigPathInfo.TargetInfo.Id);
				DisplayConfigSourceDeviceName sourceDeviceName = GetSourceDeviceName(displayConfigPathInfo.SourceInfo.AdapterId, displayConfigPathInfo.SourceInfo.Id);
				string text = (targetDeviceName.MonitorDevicePath ?? string.Empty).TrimEnd('\0');
				string text2 = (sourceDeviceName.ViewGdiDeviceName ?? string.Empty).TrimEnd('\0');
				if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2) && monitorBounds.TryGetValue(text2, out var value))
				{
					list.Add(new ActiveOutput(text, text2, value.Left, value.Top, value.Right, value.Bottom));
				}
			}
		}
		return list;
	}

	private static IReadOnlyDictionary<string, Rect> ReadMonitorBounds()
	{
		Dictionary<string, Rect> result = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
		Exception failure = null;
		MonitorEnumProc callback = delegate(nint monitor, nint _, nint _, nint _)
		{
			MonitorInfoEx monitorInfoEx = default(MonitorInfoEx);
			monitorInfoEx.Size = Marshal.SizeOf<MonitorInfoEx>();
			monitorInfoEx.DeviceName = string.Empty;
			MonitorInfoEx info = monitorInfoEx;
			if (!GetMonitorInfo(monitor, ref info))
			{
				failure = new Win32Exception(Marshal.GetLastWin32Error(), "Could not read current monitor bounds.");
				return false;
			}
			if (!string.IsNullOrWhiteSpace(info.DeviceName))
			{
				result[info.DeviceName] = info.Monitor;
			}
			return true;
		};
		if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
		{
			throw failure ?? new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate current monitor bounds.");
		}
		return result;
	}

	private static DisplayConfigTargetDeviceName GetTargetDeviceName(Luid adapterId, uint targetId)
	{
		DisplayConfigTargetDeviceName displayConfigTargetDeviceName = default(DisplayConfigTargetDeviceName);
		displayConfigTargetDeviceName.Header = new DisplayConfigDeviceInfoHeader
		{
			Type = 2,
			Size = Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
			AdapterId = adapterId,
			Id = targetId
		};
		displayConfigTargetDeviceName.MonitorFriendlyDeviceName = string.Empty;
		displayConfigTargetDeviceName.MonitorDevicePath = string.Empty;
		DisplayConfigTargetDeviceName request = displayConfigTargetDeviceName;
		int num = DisplayConfigGetDeviceInfo(ref request);
		if (num != 0)
		{
			throw new Win32Exception(num, "Could not read display target identity.");
		}
		return request;
	}

	private static DisplayConfigSourceDeviceName GetSourceDeviceName(Luid adapterId, uint sourceId)
	{
		DisplayConfigSourceDeviceName displayConfigSourceDeviceName = default(DisplayConfigSourceDeviceName);
		displayConfigSourceDeviceName.Header = new DisplayConfigDeviceInfoHeader
		{
			Type = 1,
			Size = Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
			AdapterId = adapterId,
			Id = sourceId
		};
		displayConfigSourceDeviceName.ViewGdiDeviceName = string.Empty;
		DisplayConfigSourceDeviceName request = displayConfigSourceDeviceName;
		int num = DisplayConfigGetDeviceInfo(ref request);
		if (num != 0)
		{
			throw new Win32Exception(num, "Could not read display source identity.");
		}
		return request;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayConfigPathInfo[] paths, ref uint modeCount, nint modes, nint currentTopologyId);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName request);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName request);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW", SetLastError = true)]
	private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);
}
