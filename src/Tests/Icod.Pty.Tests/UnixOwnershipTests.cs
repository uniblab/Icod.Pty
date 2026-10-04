using System.Runtime.InteropServices;
using Icod.Pty.Unix;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class UnixOwnershipTests {
	[Theory]
	[InlineData("ignore")] [InlineData("no-cldwait")]
	public async Task Unsafe_SIGCHLD_host_is_rejected_before_spawn(string guard) {
		if (OperatingSystem.IsWindows()) return;
		using System.Diagnostics.Process probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PtyTestSupport.DotNet) {
			UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
			ArgumentList = { Path.Combine(AppContext.BaseDirectory, "child", "Icod.Pty.TestChild.dll"), "scope-host-guard", guard }
		})!;
		Task<string> output = probe.StandardOutput.ReadToEndAsync(), error = probe.StandardError.ReadToEndAsync();
		try {
			await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
			Assert.True(probe.ExitCode == 0, await error); Assert.Contains("HOST-GUARD-PASSED", await output);
		} finally { if (!probe.HasExited) { probe.Kill(); await probe.WaitForExitAsync(); } }
	}
	[Fact]
	public async Task Exec_failure_preserves_native_diagnostic_and_releases_descriptors() {
		if (OperatingSystem.IsWindows()) return;
		string file = Path.GetTempFileName();
		try {
			File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
			for (int i = 0; i < 3; i++) {
				IOException error = await Assert.ThrowsAsync<IOException>(() => PtyProcess.StartAsync(new(file) { Ownership = PtyProcessOwnership.PlatformScope }));
				Assert.Contains("execve", error.Message);
			}
		} finally { File.Delete(file); }
	}
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
