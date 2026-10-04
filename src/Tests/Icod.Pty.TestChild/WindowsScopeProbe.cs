using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static class WindowsScopeProbe {
	private static string DotNet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath!;
	internal static async Task<int> RunAsync(string scenario) {
		string directory = Path.Combine(Path.GetTempPath(), "icod-job-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		nint job = CreateJobObjectW(0, null), outerJob = 0, descendant = 0;
		ProcessInformation child = default;
		try {
			Check(job != 0, "CreateJobObject");
			Limits limits = new() { Basic = new() { Flags = 0x2000 } };
			Check(Marshal.SizeOf<Limits>() == 144, "job ABI");
			Check(SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Limits>()), "SetInformationJobObject");
			StartupInfo startup = new() { Size = (uint)Marshal.SizeOf<StartupInfo>() };
			StringBuilder command = new(Quote(DotNet) + " " + Quote(typeof(WindowsScopeProbe).Assembly.Location) + " scope-windows-parent " + Quote(directory));
			Check(CreateProcessW(DotNet, command, 0, 0, false, 4 | 0x08000000, 0, directory, ref startup, out child), "CreateProcess suspended");
			bool suspended = WaitForSingleObject(child.Process, 0) == 258 && !File.Exists(Path.Combine(directory, "parent-ready"));
			if (scenario == "assignment-failure") {
				Check(!AssignProcessToJobObject(0, child.Process), "invalid-job assignment must fail");
				Check(TerminateProcess(child.Process, 1), "rollback child");
			} else {
				if (scenario == "nested") {
					outerJob = CreateJobObjectW(0, null); Check(outerJob != 0, "Create outer job");
					Check(AssignProcessToJobObject(outerJob, child.Process), "Assign outer job");
				}
				Check(AssignProcessToJobObject(job, child.Process), "AssignProcessToJobObject");
				Check(IsProcessInJob(child.Process, job, out bool assigned) && assigned, "primary job membership");
				if (scenario == "resume-failure") {
					Check(ResumeThread(0) == uint.MaxValue, "invalid-thread resume must fail");
					Check(TerminateJobObject(job, 1), "rollback job");
				} else {
					Check(ResumeThread(child.Thread) == 1, "ResumeThread");
					await WaitFile(directory, "child-ready");
					int pid = int.Parse(File.ReadAllText(Path.Combine(directory, "child-ready")), System.Globalization.CultureInfo.InvariantCulture);
					descendant = OpenProcess(0x100000 | 0x1000, false, pid);
					Check(descendant != 0, "OpenProcess descendant");
					Check(IsProcessInJob(descendant, job, out bool member) && member, "descendant job membership");
					File.WriteAllText(Path.Combine(directory, "exit-primary"), "exit");
					Check(WaitForSingleObject(child.Process, 10000) == 0, "primary exit");
					Check(WaitForSingleObject(descendant, 0) == 258, "descendant survives primary");
					Check(TerminateJobObject(job, 1), "terminate surviving scope");
					Check(WaitForSingleObject(descendant, 10000) == 0, "descendant exit");
				}
			}
			Check(WaitForSingleObject(child.Process, 10000) == 0, "primary cleanup");
			Console.WriteLine(JsonSerializer.Serialize(new { SuspendedBeforeAssignment = suspended, CleanupConfirmed = true,
				ChildRan = File.Exists(Path.Combine(directory, "parent-ready")) }));
			return 0;
		} finally {
			if (job != 0) { TerminateJobObject(job, 1); CloseHandle(job); }
			if (outerJob != 0) CloseHandle(outerJob);
			if (child.Process != 0) { TerminateProcess(child.Process, 1); WaitForSingleObject(child.Process, 10000); CloseHandle(child.Process); }
			if (child.Thread != 0) CloseHandle(child.Thread);
			if (descendant != 0) CloseHandle(descendant);
			Directory.Delete(directory, true);
		}
	}
	internal static async Task<int> ParentAsync(string directory) {
		File.WriteAllText(Path.Combine(directory, "parent-ready"), "ready");
		using Process child = Process.Start(new ProcessStartInfo(DotNet) {
			UseShellExecute = false, CreateNoWindow = true,
			ArgumentList = { typeof(WindowsScopeProbe).Assembly.Location, "scope-windows-child", directory }
		})!;
		await WaitFile(directory, "exit-primary"); return 37;
	}
	internal static async Task<int> ChildAsync(string directory) {
		string ready = Path.Combine(directory, "child-ready");
		File.WriteAllText(ready + ".tmp", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
		File.Move(ready + ".tmp", ready);
		await Task.Delay(TimeSpan.FromSeconds(20)); return 0;
	}
	private static async Task WaitFile(string directory, string name) {
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
		while (!File.Exists(Path.Combine(directory, name))) await Task.Delay(10, timeout.Token);
	}
	private static void Check(bool condition, string operation) { if (!condition) throw new IOException(operation + ": " + Marshal.GetLastPInvokeError()); }
	private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
	[StructLayout(LayoutKind.Sequential)] private struct BasicLimits {
		internal long ProcessTime, JobTime; internal uint Flags; internal nuint MinWorkingSet, MaxWorkingSet;
		internal uint ActiveProcesses; internal nuint Affinity; internal uint Priority, Scheduling;
	}
	[StructLayout(LayoutKind.Sequential)] private struct Limits {
		internal BasicLimits Basic; internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
		internal nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
	}
	[StructLayout(LayoutKind.Sequential)] private struct StartupInfo {
		internal uint Size; internal nint Reserved, Desktop, Title; internal uint X, Y, Width, Height, Columns, Rows, Fill, Flags;
		internal ushort ShowWindow, ReservedSize; internal nint ReservedBytes, Input, Output, Error;
	}
	[StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { internal nint Process, Thread; internal uint ProcessId, ThreadId; }
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateJobObjectW(nint security, string? name);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(nint job, int information, ref Limits limits, uint length);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(nint job, nint process);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsProcessInJob(nint process, nint job, [MarshalAs(UnmanagedType.Bool)] out bool result);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateJobObject(nint job, uint code);
	[DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(nint thread);
	[DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(nint process, uint code);
	[DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CreateProcessW(string file, StringBuilder command, nint processSecurity, nint threadSecurity,
		[MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, nint environment, string directory, ref StartupInfo startup, out ProcessInformation process);
}
