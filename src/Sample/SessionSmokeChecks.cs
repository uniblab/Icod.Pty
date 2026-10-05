using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Icod.Pty.Sample;

internal static class SessionSmokeChecks {
	internal static async Task<int> RunAsync() {
		PtyStartInfo start = new(OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : "/bin/sh");
		start.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c"); start.ArgumentList.Add("echo ICOD-PTY-SESSION");
		using MemoryStream output = new(); await using PtySession session = await PtySession.StartAsync(start, new(output));
		PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(30));
		string text = Encoding.UTF8.GetString(output.ToArray());
		if (result.ExitCode != 0 || result.OutputStatus != PtySessionOutputStatus.EndOfStream || !text.Contains("ICOD-PTY-SESSION", StringComparison.Ordinal))
			throw new IOException("PTY session smoke check failed: " + text);
		Console.WriteLine("PTY session smoke check passed."); return 0;
	}
	internal static async Task<int> RunScopeAsync() {
		string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "icod-session-scope-smoke-" + Guid.NewGuid().ToString("N"))).FullName;
		try {
			PtyStartInfo start = Self("--session-scope-parent", directory); start.Ownership = PtyProcessOwnership.PlatformScope;
			using MemoryStream output = new(); await using PtySession session = await PtySession.StartAsync(start, new(output) { DrainTimeout = TimeSpan.FromMilliseconds(250) });
			await Wait(directory, "parent-ready"); await Wait(directory, "child-ready");
			using Process child = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(directory, "child-ready")), System.Globalization.CultureInfo.InvariantCulture));
			if (OperatingSystem.IsWindows()) _ = child.SafeHandle;
			File.WriteAllText(Path.Combine(directory, "exit-primary"), "exit"); PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(30));
			await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
			PtySessionOutputStatus expected = OperatingSystem.IsLinux() ? PtySessionOutputStatus.TimedOut : PtySessionOutputStatus.EndOfStream;
			if (result.ExitCode != 37 || result.OutputStatus != expected) throw new IOException($"PTY session scope smoke check failed: exit={result.ExitCode}, output={result.OutputStatus}, reason={result.Reason}.");
			Console.WriteLine("PTY session scope smoke check passed."); return 0;
		} finally {
			File.WriteAllText(Path.Combine(directory, "stop-child"), "stop");
			try { Directory.Delete(directory, true); } catch (IOException) { }
		}
	}
	internal static async Task<int> ParentAsync(string directory) {
		using Process child = Process.Start(ToProcessStartInfo(Self("--session-scope-descendant", directory))) ?? throw new IOException("Cannot start scope descendant.");
		await Wait(directory, "child-ready"); File.WriteAllText(Path.Combine(directory, "parent-ready"), "ready"); await Wait(directory, "exit-primary"); return 37;
	}
	internal static async Task<int> DescendantAsync(string directory) {
		using System.Runtime.InteropServices.PosixSignalRegistration? hangup = OperatingSystem.IsWindows() ? null :
			System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGHUP, signal => signal.Cancel = true);
		string ready = Path.Combine(directory, "child-ready");
		File.WriteAllText(ready + ".tmp", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)); File.Move(ready + ".tmp", ready);
		await Wait(directory, "stop-child", TimeSpan.FromSeconds(60)); return 0;
	}
	private static PtyStartInfo Self(params string[] arguments) {
		string executable = Environment.ProcessPath ?? throw new IOException("Cannot identify the sample executable."); PtyStartInfo start = new(executable);
		if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
			start.ArgumentList.Add(Assembly.GetEntryAssembly()?.Location ?? throw new IOException("Cannot identify the sample assembly."));
		foreach (string argument in arguments) start.ArgumentList.Add(argument); return start;
	}
	private static ProcessStartInfo ToProcessStartInfo(PtyStartInfo info) {
		ProcessStartInfo result = new(info.FileName) { UseShellExecute = false }; foreach (string argument in info.ArgumentList) result.ArgumentList.Add(argument); return result;
	}
	private static async Task Wait(string directory, string name, TimeSpan? timeout = null) {
		using CancellationTokenSource deadline = new(timeout ?? TimeSpan.FromSeconds(10));
		while (!File.Exists(Path.Combine(directory, name))) await Task.Delay(10, deadline.Token);
	}
}
