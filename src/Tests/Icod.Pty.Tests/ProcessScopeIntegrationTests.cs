using System.Diagnostics;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class ProcessScopeIntegrationTests {
	[Fact]
	public async Task Darwin_repeated_cleanup_after_descendant_exit() {
		if (!OperatingSystem.IsMacOS()) return;
		for (int iteration = 0; iteration < 12; iteration++) await Owned_cleanup_stops_known_descendant_after_primary_exit(false);
	}
	[Theory]
	[InlineData(false)] [InlineData(true)]
	public async Task Owned_cleanup_stops_known_descendant_after_primary_exit(bool dispose) {
		using Fixture fixture = new();
		using PtyProcess process = await fixture.Start(); Task<string> drain = PtyTestSupport.Drain(process.Output);
		await fixture.Ready(); using Process child = fixture.ObserveChild();
		await fixture.ExitPrimary(process); Assert.False(child.HasExited);
		if (dispose) await Task.WhenAll(Task.Run(process.Dispose), Task.Run(process.Dispose));
		else { process.RequestTermination(PtyProcessTarget.OwnedScope); process.RequestTermination(PtyProcessTarget.OwnedScope); }
		await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
		Assert.Equal(37, process.ExitCode);
		try { await drain.WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception error) when (dispose && error is IOException or ObjectDisposedException) { }
	}
	[Theory]
	[InlineData("detached")] [InlineData("other-group")] [InlineData("pipeline")]
	public async Task Escaped_groups_remain_outside_initial_scope(string mode) {
		if (OperatingSystem.IsWindows()) return;
		using Fixture fixture = new(); using PtyProcess process = await fixture.Start(mode);
		Task<string> drain = PtyTestSupport.Drain(process.Output); await fixture.Ready(); using Process child = fixture.ObserveChild();
		await fixture.ExitPrimary(process); process.RequestTermination(PtyProcessTarget.OwnedScope);
		Assert.False(child.HasExited); fixture.StopChild(); await fixture.Wait("child-stopped");
		await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); await drain;
	}
	[Fact]
	public async Task Raw_native_interrupt_and_ignored_term_do_not_require_input_bytes() {
		if (OperatingSystem.IsWindows()) return;
		using Fixture fixture = new(); using PtyProcess process = await fixture.Start("raw-interrupt");
		Task<string> drain = PtyTestSupport.Drain(process.Output); await fixture.Ready();
		await fixture.ExitPrimary(process);
		process.SendSignal(PtySignal.Interrupt, PtyProcessTarget.OwnedScope); await fixture.Wait("signal-int");
		process.SendSignal(PtySignal.Terminate, PtyProcessTarget.OwnedScope); await fixture.Wait("child-stopped"); await drain;
	}
	[Fact]
	public async Task Ignored_termination_can_be_followed_by_force() {
		if (OperatingSystem.IsWindows()) return;
		using Fixture fixture = new(); using PtyProcess process = await fixture.Start("ignore-term");
		Task<string> drain = PtyTestSupport.Drain(process.Output); await fixture.Ready(); using Process child = fixture.ObserveChild();
		await fixture.ExitPrimary(process); process.SendSignal(PtySignal.Terminate, PtyProcessTarget.OwnedScope);
		await fixture.Wait("signal-term"); Assert.False(child.HasExited);
		process.RequestTermination(PtyProcessTarget.OwnedScope); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); await drain;
	}
	[Fact]
	public async Task Unrelated_child_is_untouched_by_owned_cleanup() {
		using PtyProcess unrelated = await PtyProcess.StartAsync(PtyTestSupport.Child()); await PtyTestSupport.ReadUntil(unrelated.Output, "READY:ok");
		using Fixture fixture = new(); using PtyProcess process = await fixture.Start();
		Task<string> drain = PtyTestSupport.Drain(process.Output); await fixture.Ready(); process.RequestTermination(PtyProcessTarget.OwnedScope);
		await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); await drain;
		Assert.False(unrelated.HasExited); await unrelated.Input.WriteAsync(PtyTestSupport.Line("still-alive")); await PtyTestSupport.ReadUntil(unrelated.Output, "ECHO:still-alive");
	}
	[Fact]
	public void Cleanup_failures_do_not_skip_remaining_resources() {
		int released = 0; IOException expected = new("control denied");
		IOException actual = Assert.Throws<IOException>(() => CleanupActions.Run(() => throw expected, () => released++, () => released++));
		Assert.Same(expected, actual); Assert.Equal(2, released);
	}
	private sealed class Fixture : IDisposable {
		private readonly string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "icod-scope-integration-" + Guid.NewGuid().ToString("N"))).FullName;
		internal Task<PtyProcess> Start(string mode = "same") {
			PtyStartInfo info = OperatingSystem.IsWindows() ? PtyTestSupport.Child("scope-windows-parent", directory) : PtyTestSupport.Child("scope-parent", directory, mode);
			if (mode == "pipeline") {
				static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
				info = new("/bin/sh"); info.ArgumentList.Add("-c");
				info.ArgumentList.Add("set -m; " + Quote(PtyTestSupport.DotNet) + " " + Quote(Path.Combine(AppContext.BaseDirectory, "child", "Icod.Pty.TestChild.dll")) +
					" scope-child " + Quote(directory) + " same | cat & printf ready > " + Quote(Path.Combine(directory, "parent-ready")) +
					"; while [ ! -f " + Quote(Path.Combine(directory, "exit-primary")) + " ]; do sleep 0.02; done; exit 37");
			}
			info.Ownership = PtyProcessOwnership.PlatformScope; return PtyProcess.StartAsync(info);
		}
		internal async Task Ready() { await Wait("parent-ready"); await Wait("child-ready"); }
		internal Process ObserveChild() {
			Process child = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(directory, OperatingSystem.IsWindows() ? "child-ready" : "child-pid")), System.Globalization.CultureInfo.InvariantCulture));
			if (OperatingSystem.IsWindows()) _ = child.SafeHandle; return child;
		}
		internal async Task ExitPrimary(PtyProcess process) { File.WriteAllText(Path.Combine(directory, "exit-primary"), "exit"); Assert.Equal(37, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10))); }
		internal async Task Wait(string name) { using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30)); while (!File.Exists(Path.Combine(directory, name))) await Task.Delay(10, timeout.Token); }
		internal void StopChild() => File.WriteAllText(Path.Combine(directory, "stop-child"), "stop");
		public void Dispose() { StopChild(); }
	}
}
