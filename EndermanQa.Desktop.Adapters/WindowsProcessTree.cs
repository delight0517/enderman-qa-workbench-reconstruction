using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace EndermanQa.Desktop.Adapters;

internal static class WindowsProcessTree
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct ProcessEntry32
	{
		public uint Size;

		public uint Usage;

		public uint ProcessId;

		public nint DefaultHeapId;

		public uint ModuleId;

		public uint Threads;

		public uint ParentProcessId;

		public int BasePriority;

		public uint Flags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string ExecutableFile;
	}

	private const uint SnapshotProcesses = 2u;

	private static readonly nint InvalidHandle = new IntPtr(-1);

	public static bool IsDescendantOf(int processId, int ancestorProcessId)
	{
		if (!OperatingSystem.IsWindows() || processId <= 0 || ancestorProcessId <= 0)
		{
			return false;
		}
		nint num = CreateToolhelp32Snapshot(2u, 0u);
		if (num == InvalidHandle)
		{
			return false;
		}
		try
		{
			Dictionary<int, int> dictionary = new Dictionary<int, int>();
			ProcessEntry32 processEntry = default(ProcessEntry32);
			processEntry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
			ProcessEntry32 entry = processEntry;
			if (!Process32First(num, ref entry))
			{
				return false;
			}
			int num2;
			checked
			{
				do
				{
					dictionary[(int)entry.ProcessId] = (int)entry.ParentProcessId;
				}
				while (Process32Next(num, ref entry));
				num2 = processId;
			}
			for (int i = 0; i < 16; i++)
			{
				if (!dictionary.TryGetValue(num2, out var value))
				{
					break;
				}
				if (value == ancestorProcessId)
				{
					return true;
				}
				if (value <= 0 || value == num2)
				{
					return false;
				}
				num2 = value;
			}
			return false;
		}
		finally
		{
			CloseHandle(num);
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint handle);
}
