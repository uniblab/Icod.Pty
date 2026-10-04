using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

// Native lifetime experiments run in a separate fixture process, never in the test runner.
internal static class ProcessScopeFixture {
	private static string DotNet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath!;
	private static int WaitFlags => OperatingSystem.IsMacOS() ? 0x25 : 0x01000005; // EXITED | NOWAIT | NOHANG
	private static int PidOffset => OperatingSystem.IsMacOS() ? 12 : 16;
	private static int StatusOffset => OperatingSystem.IsMacOS() ? 20 : 24;
	internal static async Task<int> AutoReapProbeAsync() {
		int sigchld = OperatingSystem.IsMacOS() ? 20 : 17;
		if (signal(sigchld, 1) == -1) throw new IOException("Set initial SIG_IGN failed.");
		byte[] action = new byte[256];
		if (sigaction(sigchld, 0, action) != 0) throw new IOException("Read current SIGCHLD action failed.");
		bool ignoredSignalDetected = BitConverter.ToInt64(action, 0) == 1;
		using NativeVector args = new(["/bin/sh", "-c", "exit 37"]);
		using NativeVector environment = new(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().Select(p => p.Key + "=" + p.Value));
		int error = posix_spawn(out int pid, "/bin/sh", 0, 0, args.Pointer, environment.Pointer);
		if (error != 0) throw new IOException("Spawn auto-reap probe: " + error);
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
		bool autoReaped = false;
		try {
			while (true) {
				if (waitid(1, pid, new byte[128], WaitFlags) < 0) {
					if (Marshal.GetLastPInvokeError() != 10) throw new IOException("Unexpected waitid error.");
					autoReaped = true; break;
				}
				await Task.Delay(10, timeout.Token);
			}
		} finally { waitpid(pid, out _, 1); }
		Console.WriteLine(JsonSerializer.Serialize(new { AutoReaped = autoReaped, IgnoredSignalDetected = ignoredSignalDetected }));
		return 0;
	}
	internal static async Task<int> ProbeAsync(string scenario) {
		string directory = Path.Combine(Path.GetTempPath(), "icod-scope-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		int pid = 0;
		bool reaped = false;
		try {
			using NativeVector args = new([DotNet, typeof(ProcessScopeFixture).Assembly.Location, "scope-parent", directory]);
			using NativeVector environment = new(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().Select(p => p.Key + "=" + p.Value));
			int spawnError = posix_spawn(out pid, DotNet, 0, 0, args.Pointer, environment.Pointer);
			if (spawnError != 0) throw new IOException("posix_spawn: " + spawnError);
			await WaitFile(directory, "parent-ready"); await WaitFile(directory, "child-ready");
			int childGroup = int.Parse(File.ReadAllText(Path.Combine(directory, "child-ready")), System.Globalization.CultureInfo.InvariantCulture);
			if (childGroup != pid || getpgid(pid) != pid || getsid(pid) != pid) throw new IOException("Initial group/session mismatch.");
			// Initialize the normal .NET child reaper before observing the native child.
			using Process managed = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "exit 11" }, UseShellExecute = false })!;
			await managed.WaitForExitAsync();
			File.WriteAllText(Path.Combine(directory, "exit-primary"), "exit");
			byte[] status = await ObserveExit(pid);
			int code = BitConverter.ToInt32(status, StatusOffset);
			// Darwin getpgid excludes zombies even while waitid retains the child identity.
			// Group membership was established above while live; group delivery is tested below.
			bool retained = BitConverter.ToInt32(status, PidOffset) == pid;
			// Deliver an unrelated managed-child SIGCHLD while retaining the native zombie.
			using Process second = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "exit 12" }, UseShellExecute = false })!;
			await second.WaitForExitAsync();
			retained &= BitConverter.ToInt32(await ObserveExit(pid), PidOffset) == pid;
			bool interferenceDetected = false;
			if (scenario == "reaper") {
				// Deliberately model another component consuming our wait record.
				if (waitpid(pid, out _, 0) != pid) throw new IOException("Competing reap failed.");
				reaped = true;
				byte[] info = new byte[128];
				interferenceDetected = waitid(1, pid, info, WaitFlags) == -1 && Marshal.GetLastPInvokeError() == 10;
				// Never signal the cached group after losing the anchor; ask the fixture itself to stop.
				File.WriteAllText(Path.Combine(directory, "stop-child"), "stop");
			} else {
				if (pid <= 1 || kill(-pid, 15) != 0) throw new IOException("Owned group SIGTERM failed.");
			}
			await WaitFile(directory, "child-stopped");
			if (!reaped) { if (waitpid(pid, out _, 0) != pid) throw new IOException("Final reap failed."); reaped = true; }
			bool reapedOnce = waitpid(pid, out _, 1) == -1 && Marshal.GetLastPInvokeError() == 10;
			Console.WriteLine(JsonSerializer.Serialize(new { ExitCode = code, Retained = retained,
				ManagedChildCollected = managed.ExitCode == 11 && second.ExitCode == 12,
				ReapedOnce = reapedOnce, InterferenceDetected = interferenceDetected }));
			return 0;
		} finally {
			File.WriteAllText(Path.Combine(directory, "stop-child"), "stop");
			if (pid > 1 && !reaped) {
				// The native child remains ours until this specific wait consumes it.
				if (getpgid(pid) == pid) kill(-pid, 9); else kill(pid, 9);
				waitpid(pid, out _, 0);
			}
			// Keep failure records available until the bounded fixture lease ends.
			if (File.Exists(Path.Combine(directory, "child-stopped"))) Directory.Delete(directory, true);
		}
	}
	internal static async Task<int> ParentAsync(string directory) {
		if (getsid(0) != Environment.ProcessId && setsid() != Environment.ProcessId) throw new IOException("setsid failed.");
		using Process child = Process.Start(new ProcessStartInfo(DotNet) {
			UseShellExecute = false,
			ArgumentList = { typeof(ProcessScopeFixture).Assembly.Location, "scope-child", directory }
		})!;
		await WaitFile(directory, "child-ready");
		File.WriteAllText(Path.Combine(directory, "parent-ready"), "ready");
		await WaitFile(directory, "exit-primary");
		return 37;
	}
	internal static async Task<int> ChildAsync(string directory) {
		TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using PosixSignalRegistration term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; stopped.TrySetResult(); });
		using PosixSignalRegistration hup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => context.Cancel = true);
		string ready = Path.Combine(directory, "child-ready");
		File.WriteAllText(ready + ".tmp", getpgid(0).ToString(System.Globalization.CultureInfo.InvariantCulture));
		File.Move(ready + ".tmp", ready);
		using CancellationTokenSource lease = new(TimeSpan.FromSeconds(20));
		try {
			while (!stopped.Task.IsCompleted && !File.Exists(Path.Combine(directory, "stop-child"))) await Task.Delay(10, lease.Token);
		} catch (OperationCanceledException) when (lease.IsCancellationRequested) { }
		File.WriteAllText(Path.Combine(directory, "child-stopped"), "stopped");
		return 0;
	}
	private static async Task WaitFile(string directory, string name) {
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
		while (!File.Exists(Path.Combine(directory, name))) await Task.Delay(10, timeout.Token);
	}
	private static async Task<byte[]> ObserveExit(int pid) {
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
		while (true) {
			byte[] info = new byte[128]; // Linux 128; Darwin 104 on supported 64-bit ABIs.
			if (waitid(1, pid, info, WaitFlags) != 0) throw new IOException("waitid: " + Marshal.GetLastPInvokeError());
			if (BitConverter.ToInt32(info, PidOffset) == pid) return info;
			await Task.Delay(10, timeout.Token);
		}
	}
	private sealed class NativeVector : IDisposable {
		private readonly List<nint> allocations = [];
		internal nint Pointer { get; }
		internal NativeVector(IEnumerable<string> values) {
			string[] items = values.ToArray();
			Pointer = Marshal.AllocHGlobal((items.Length + 1) * nint.Size); allocations.Add(Pointer);
			for (int i = 0; i < items.Length; i++) {
				nint value = Marshal.StringToCoTaskMemUTF8(items[i]); allocations.Add(value);
				Marshal.WriteIntPtr(Pointer, i * nint.Size, value);
			}
			Marshal.WriteIntPtr(Pointer, items.Length * nint.Size, 0);
		}
		public void Dispose() { for (int i = 1; i < allocations.Count; i++) Marshal.FreeCoTaskMem(allocations[i]); Marshal.FreeHGlobal(Pointer); }
	}
	[DllImport("libc", SetLastError = true)] private static extern int posix_spawn(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint actions, nint attributes, nint argv, nint environment);
	[DllImport("libc", SetLastError = true)] private static extern int waitid(int type, int pid, [Out] byte[] info, int flags);
	[DllImport("libc", SetLastError = true)] private static extern int waitpid(int pid, out int status, int flags);
	[DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
	[DllImport("libc", SetLastError = true)] private static extern int setsid();
	[DllImport("libc", SetLastError = true)] private static extern int getpgid(int pid);
	[DllImport("libc", SetLastError = true)] private static extern int getsid(int pid);
	[DllImport("libc", SetLastError = true)] private static extern nint signal(int signal, nint handler);
	[DllImport("libc", SetLastError = true)] private static extern int sigaction(int signal, nint action, [Out] byte[] previous);
}
