using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Icod.Pty.Windows;

internal static class WindowsNative {
	[StructLayout(LayoutKind.Sequential)]
	internal struct Coord { internal short X, Y; }
	[StructLayout(LayoutKind.Sequential)]
	internal struct StartupInfo {
		internal uint Size;
		internal nint Reserved, Desktop, Title;
		internal uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
		internal ushort ShowWindow, ReservedSize;
		internal nint ReservedBytes, StandardInput, StandardOutput, StandardError;
	}
	[StructLayout(LayoutKind.Sequential)]
	internal struct StartupInfoEx { internal StartupInfo Startup; internal nint Attributes; }
	[StructLayout(LayoutKind.Sequential)]
	internal struct ProcessInformation { internal nint Process, Thread; internal uint ProcessId, ThreadId; }
	[StructLayout(LayoutKind.Sequential)]
	internal struct BasicLimitInformation {
		internal long ProcessTime, JobTime;
		internal uint LimitFlags;
		internal nuint MinWorkingSet, MaxWorkingSet;
		internal uint ActiveProcesses;
		internal nuint Affinity;
		internal uint Priority, Scheduling;
	}
	[StructLayout(LayoutKind.Sequential)]
	internal struct ExtendedLimitInformation {
		internal BasicLimitInformation Basic;
		internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
		internal nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
	}
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeJobHandle CreateJobObjectW(nint security, string? name);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool SetInformationJobObject(SafeJobHandle job, int kind, ref ExtendedLimitInformation limits, uint length);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool IsProcessInJob(SafeProcessHandle process, SafeJobHandle job, [MarshalAs(UnmanagedType.Bool)] out bool result);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool TerminateJobObject(SafeJobHandle job, uint code);
	[DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(nint thread);
	[DllImport("kernel32.dll", SetLastError = true)] internal static extern uint GetProcessId(SafeProcessHandle process);
	[DllImport("kernel32.dll")] internal static extern int CreatePseudoConsole(Coord size, SafePipeHandle input, SafePipeHandle output, uint flags, out SafePseudoConsoleHandle console);
	[DllImport("kernel32.dll")] internal static extern int ResizePseudoConsole(SafePseudoConsoleHandle console, Coord size);
	[DllImport("kernel32.dll")] internal static extern void ClosePseudoConsole(nint console);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nuint size);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint size, nint previous, nint returned);
	[DllImport("kernel32.dll")] internal static extern void DeleteProcThreadAttributeList(nint list);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool CreateProcessW(string application, StringBuilder commandLine, nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, nint environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
	[DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool TerminateProcess(SafeProcessHandle process, uint code);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool CloseHandle(nint handle);
}
internal sealed class SafePseudoConsoleHandle : SafeHandle {
	public SafePseudoConsoleHandle() : base(0, true) { }
	public override bool IsInvalid => handle == 0 || handle == -1;
	protected override bool ReleaseHandle() { WindowsNative.ClosePseudoConsole(handle); return true; }
}
