using System.Runtime.InteropServices;
using Icod.Pty.Unix;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class UnixOwnershipTests {
	[Fact]
	public async Task Owned_group_survives_primary_exit() {
		if (OperatingSystem.IsWindows()) return;
		string directory = Path.Combine(Path.GetTempPath(), "icod-owned-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
		try {
			PtyStartInfo info = PtyTestSupport.Child("scope-parent", directory); info.Ownership = PtyProcessOwnership.PlatformScope;
			using IPtyBackend backend = await UnixBackend.StartAsync(LaunchConfiguration.Capture(info), default);
			Task drain = PtyTestSupport.Drain(backend.Output);
			await WaitFile(directory, "parent-ready");
			File.WriteAllText(Path.Combine(directory, "exit-primary"), "exit");
			Assert.Equal(37, await backend.Exit.WaitAsync(TimeSpan.FromSeconds(10)));
			Assert.False(File.Exists(Path.Combine(directory, "child-stopped")));
			Assert.Equal(PtyControlStatus.Requested, backend.SendSignal(PtySignal.Terminate, PtyProcessTarget.OwnedScope).Status);
			await WaitFile(directory, "child-stopped");
			await drain.WaitAsync(TimeSpan.FromSeconds(10));
			backend.Dispose(); backend.Dispose();
			Assert.Equal(-1, waitpid(backend.ProcessId, out _, 1)); Assert.Equal(10, Marshal.GetLastPInvokeError());
		} finally { File.WriteAllText(Path.Combine(directory, "stop-child"), "stop"); }
	}
	[Fact]
	public async Task Owned_exit_preserves_code_and_final_output() {
		if (OperatingSystem.IsWindows()) return;
		PtyStartInfo info = PtyTestSupport.Child("exit"); info.Ownership = PtyProcessOwnership.PlatformScope;
		using PtyProcess process = await PtyProcess.StartAsync(info);
		Task<string> drain = PtyTestSupport.Drain(process.Output);
		Assert.Equal(37, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.Contains("FINAL-MARKER", await drain);
	}
	private static async Task WaitFile(string directory, string name) {
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
		while (!File.Exists(Path.Combine(directory, name))) await Task.Delay(10, timeout.Token);
	}
	[DllImport("libc", SetLastError = true)] private static extern int waitpid(int pid, out int status, int flags);
}
