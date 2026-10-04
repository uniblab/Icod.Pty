using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Icod.Pty.Sample;

internal static class ProcessScopeSmokeChecks {
	internal static async Task<int> RunAsync() {
		string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "icod-scope-smoke-" + Guid.NewGuid().ToString("N"))).FullName;
		using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(25));
		try {
			PtyStartInfo start = Self("--scope-parent", directory); start.Ownership = PtyProcessOwnership.PlatformScope;
			await using PtyProcess process = await PtyProcess.StartAsync(start, deadline.Token);
			Task drain = process.Output.CopyToAsync(Stream.Null, deadline.Token);
			try {
				await WaitFile(directory, "child-ready", deadline.Token);
				using Process child = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(directory, "child-ready")), System.Globalization.CultureInfo.InvariantCulture));
				if (OperatingSystem.IsWindows()) _ = child.SafeHandle;
				File.WriteAllText(Path.Combine(directory, "exit-primary"), "exit");
				if (await process.WaitForExitAsync(deadline.Token) != 37 || child.HasExited) throw new IOException("The descendant must survive primary exit in this check.");
				if (!process.Capabilities.HasFlag(PtyProcessCapabilities.TerminateOwnedScope)) throw new IOException("Owned cleanup capability is missing.");
				PtyControlResult request = process.RequestTermination(PtyProcessTarget.OwnedScope);
				if (request.Status != PtyControlStatus.Requested) throw new IOException("Scope termination was not requested.");
				// Observe the known descendant independently of the native request result.
				await child.WaitForExitAsync(deadline.Token).WaitAsync(TimeSpan.FromSeconds(10), deadline.Token);
				await drain.WaitAsync(TimeSpan.FromSeconds(5), deadline.Token);
			} finally {
				File.WriteAllText(Path.Combine(directory, "stop-child"), "stop"); deadline.Cancel();
				try { await drain; } catch (Exception) { /* Observe the reader before session disposal. */ }
			}
		} finally { Directory.Delete(directory, true); }
		Console.WriteLine("PTY process-scope smoke check passed."); return 0;
	}
	internal static async Task<int> ParentAsync(string directory) {
		PtyStartInfo self = Self("--scope-descendant", directory);
		ProcessStartInfo start = new(self.FileName) { UseShellExecute = false, CreateNoWindow = true };
		foreach (string argument in self.ArgumentList) start.ArgumentList.Add(argument);
		using Process child = Process.Start(start) ?? throw new IOException("Cannot start scope smoke descendant.");
		using CancellationTokenSource lease = new(TimeSpan.FromSeconds(20));
		await WaitFile(directory, "exit-primary", lease.Token); return 37;
	}
	internal static async Task<int> DescendantAsync(string directory) {
		using PosixSignalRegistration? hangup = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => context.Cancel = true);
		string ready = Path.Combine(directory, "child-ready");
		File.WriteAllText(ready + ".tmp", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)); File.Move(ready + ".tmp", ready);
		using CancellationTokenSource lease = new(TimeSpan.FromSeconds(30));
		try { while (!File.Exists(Path.Combine(directory, "stop-child"))) await Task.Delay(20, lease.Token); }
		catch (OperationCanceledException) when (lease.IsCancellationRequested) { }
		return 0;
	}
	private static PtyStartInfo Self(params string[] arguments) {
		string executable = Environment.ProcessPath ?? throw new IOException("Cannot identify scope smoke executable.");
		PtyStartInfo start = new(executable);
		if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetEntryAssembly()?.Location ?? throw new IOException("Cannot identify scope smoke assembly."));
		foreach (string argument in arguments) start.ArgumentList.Add(argument);
		return start;
	}
	private static async Task WaitFile(string directory, string name, CancellationToken token) { while (!File.Exists(Path.Combine(directory, name))) await Task.Delay(10, token); }
}
