using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Adapters;

internal static class WindowsLeasedWindowRouter
{
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

	private delegate bool EnumWindowsProc(nint window, nint data);

	private const int GwlStyle = -16;

	private const long WsVisible = 268435456L;

	private const uint GwOwner = 4u;

	private const uint MonitorDefaultToNull = 0u;

	private const uint SwpNoZOrder = 4u;

	private const uint SwpNoActivate = 16u;

	private const int SwShowNoActivate = 4;

	private static readonly nint DpiAwarenessContextPerMonitorAwareV2 = new IntPtr(-4);

	internal static async Task RouteAndRevealAsync(int processId, VerifiedClientDisplayLease lease, CancellationToken cancellationToken)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Minecraft window routing requires Windows.");
		}
		if (processId <= 0)
		{
			throw new ArgumentOutOfRangeException("processId");
		}
		ArgumentNullException.ThrowIfNull(lease, "lease");
		WindowsDisplayPathMapper.ActiveOutput expected = lease.ActiveOutput;
		await ValidateOutputAndLeaseAsync(lease, expected, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(60.0);
		nint window;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using Process process = Process.GetProcessById(processId);
			if (process.HasExited)
			{
				throw new InvalidOperationException("Owned Minecraft process exited before creating its hidden window.");
			}
			List<nint> list = FindUnownedTopLevelWindows(processId);
			if (list.Count > 1)
			{
				throw new InvalidOperationException("Owned Minecraft PID has multiple unowned top-level windows; routing is ambiguous.");
			}
			if (list.Count == 1)
			{
				window = list[0];
				break;
			}
			if (DateTimeOffset.UtcNow >= deadline)
			{
				throw new TimeoutException("Owned Minecraft did not create its hidden top-level window within 60 seconds.");
			}
			await Task.Delay(250, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		EnsureOwnedHiddenWindow(window, processId);
		await ValidateOutputAndLeaseAsync(lease, expected, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		WithPerMonitorV2(delegate
		{
			if (!SetWindowPos(window, IntPtr.Zero, expected.Left, expected.Top, expected.Width, expected.Height, 20u))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not position the hidden Minecraft window on its leased output.");
			}
		});
		VerifyWindowOnOutput(window, processId, expected);
		await ValidateOutputAndLeaseAsync(lease, expected, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		EnsureOwnedHiddenWindow(window, processId);
		ShowWindow(window, 4);
		try
		{
			VerifyWindowOnOutput(window, processId, expected);
			if (!IsWindowVisible(window))
			{
				throw new InvalidOperationException("Windows did not reveal the Minecraft window after leased routing.");
			}
			await ValidateOutputAndLeaseAsync(lease, expected, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			VerifyWindowOnOutput(window, processId, expected);
		}
		catch
		{
			ShowWindow(window, 0);
			throw;
		}
	}

	internal static async Task ValidateVisiblePlacementAsync(int processId, VerifiedClientDisplayLease lease, CancellationToken cancellationToken)
	{
		WindowsDisplayPathMapper.ActiveOutput expected = lease.ActiveOutput;
		await ValidateOutputAndLeaseAsync(lease, expected, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		List<nint> list = FindUnownedTopLevelWindows(processId);
		if (list.Count != 1 || !IsWindowVisible(list[0]))
		{
			throw new InvalidOperationException("Owned Minecraft window is absent, ambiguous, or hidden; client actions are blocked.");
		}
		VerifyWindowOnOutput(list[0], processId, expected);
	}

	private static async Task ValidateOutputAndLeaseAsync(VerifiedClientDisplayLease lease, WindowsDisplayPathMapper.ActiveOutput expected, CancellationToken cancellationToken)
	{
		await lease.ValidateForSideEffectAsync("launch-client", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		WindowsDisplayPathMapper.ActiveOutput b = WindowsDisplayPathMapper.ResolveUniqueLeasedPath(lease.DevicePath, WindowsDisplayPathMapper.EnumerateActiveOutputs());
		if (!SameOutput(expected, b))
		{
			throw new InvalidOperationException("The leased output mapping changed; Minecraft remains hidden and will not be routed.");
		}
	}

	private static bool SameOutput(WindowsDisplayPathMapper.ActiveOutput a, WindowsDisplayPathMapper.ActiveOutput b)
	{
		if (a.DevicePath.Equals(b.DevicePath, StringComparison.OrdinalIgnoreCase) && a.GdiDeviceName.Equals(b.GdiDeviceName, StringComparison.OrdinalIgnoreCase) && a.Left == b.Left && a.Top == b.Top && a.Right == b.Right)
		{
			return a.Bottom == b.Bottom;
		}
		return false;
	}

	private static List<nint> FindUnownedTopLevelWindows(int processId)
	{
		List<nint> result = new List<nint>();
		if (!EnumWindows(delegate(nint window, nint _)
		{
			GetWindowThreadProcessId(window, out var processId2);
			if (processId2 == (uint)processId && GetWindow(window, 4u) == IntPtr.Zero)
			{
				result.Add(window);
			}
			return true;
		}, IntPtr.Zero))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate the owned Minecraft windows.");
		}
		return result;
	}

	private static void EnsureOwnedHiddenWindow(nint window, int processId)
	{
		if (!IsWindow(window))
		{
			throw new InvalidOperationException("Owned Minecraft window handle is no longer valid.");
		}
		GetWindowThreadProcessId(window, out var processId2);
		if (processId2 != (uint)processId || GetWindow(window, 4u) != IntPtr.Zero)
		{
			throw new InvalidOperationException("Window ownership changed; refusing to move or reveal a non-owned window.");
		}
		if ((((IntPtr)GetWindowLongPtr(window, -16)).ToInt64() & 0x10000000) != 0L || IsWindowVisible(window))
		{
			throw new InvalidOperationException("Minecraft window was visible before leased-output verification; it will not be repositioned.");
		}
	}

	private static void VerifyWindowOnOutput(nint window, int processId, WindowsDisplayPathMapper.ActiveOutput expected)
	{
		EnsureOwnedWindowIdentity(window, processId);
		nint num = MonitorFromWindow(window, 0u);
		if (num == IntPtr.Zero)
		{
			throw new InvalidOperationException("Minecraft window is not on an active Windows monitor.");
		}
		MonitorInfoEx info = new MonitorInfoEx
		{
			Size = Marshal.SizeOf<MonitorInfoEx>(),
			DeviceName = string.Empty
		};
		if (!GetMonitorInfo(num, ref info))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not verify Minecraft window monitor identity.");
		}
		if (!info.DeviceName.Equals(expected.GdiDeviceName, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Minecraft HWND is not on the output bound to its VDM lease.");
		}
		if (!GetWindowRect(window, out var rect) || rect.Left != expected.Left || rect.Top != expected.Top || rect.Right != expected.Right || rect.Bottom != expected.Bottom)
		{
			throw new InvalidOperationException("Minecraft HWND bounds do not match the leased output rectangle.");
		}
	}

	private static void EnsureOwnedWindowIdentity(nint window, int processId)
	{
		if (!IsWindow(window))
		{
			throw new InvalidOperationException("Owned Minecraft window disappeared during routing.");
		}
		GetWindowThreadProcessId(window, out var processId2);
		if (processId2 != (uint)processId)
		{
			throw new InvalidOperationException("Minecraft HWND ownership changed during routing.");
		}
	}

	private static void WithPerMonitorV2(Action action)
	{
		nint num = SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
		if (num == IntPtr.Zero)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not set per-monitor DPI context for window placement.");
		}
		try
		{
			action();
		}
		finally
		{
			SetThreadDpiAwarenessContext(num);
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool EnumWindows(EnumWindowsProc callback, nint data);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool IsWindow(nint window);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool IsWindowVisible(nint window);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint GetWindow(nint window, uint command);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
	private static extern nint GetWindowLongPtr(nint window, int index);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool ShowWindow(nint window, int command);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint MonitorFromWindow(nint window, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW", SetLastError = true)]
	private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool GetWindowRect(nint window, out Rect rect);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint SetThreadDpiAwarenessContext(nint context);
}
