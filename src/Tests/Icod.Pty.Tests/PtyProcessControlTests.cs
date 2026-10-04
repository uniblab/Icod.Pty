using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyProcessControlTests {
	[Fact]
	public async Task Owned_scope_requires_launch_opt_in() {
		ControlledBackend backend = new(); using PtyProcess process = await backend.Start();
		Assert.Throws<InvalidOperationException>(() => process.RequestTermination(PtyProcessTarget.OwnedScope));
		Assert.Equal(0, backend.ScopeTerminationCount);
	}
	[Fact]
	public async Task Invalid_control_and_disposed_control_have_no_side_effect() {
		ControlledBackend backend = new() { Ownership = PtyProcessOwnership.PlatformScope }; using PtyProcess process = await backend.Start();
		Assert.Throws<ArgumentOutOfRangeException>(() => process.RequestTermination((PtyProcessTarget)99));
		Assert.Throws<ArgumentOutOfRangeException>(() => process.SendSignal((PtySignal)99, PtyProcessTarget.OwnedScope));
		Assert.Throws<ArgumentOutOfRangeException>(() => process.SendSignal(PtySignal.Kill, (PtyProcessTarget)99));
		Assert.Equal(0, backend.TerminateCount); Assert.Equal(0, backend.ScopeTerminationCount);
		process.Dispose(); Assert.Throws<ObjectDisposedException>(() => process.RequestTermination(PtyProcessTarget.OwnedScope));
		Assert.Throws<ObjectDisposedException>(() => process.SendSignal(PtySignal.Kill, PtyProcessTarget.PrimaryProcess));
	}
	[Fact]
	public async Task Native_signal_requires_platform_support_and_anchored_ownership() {
		using PtyProcess process = await PtyProcess.StartAsync(PtyTestSupport.Child());
		await PtyTestSupport.ReadUntil(process.Output, "READY:ok");
		if (OperatingSystem.IsWindows()) Assert.Throws<PlatformNotSupportedException>(() => process.SendSignal(PtySignal.Interrupt, PtyProcessTarget.PrimaryProcess));
		else Assert.Throws<InvalidOperationException>(() => process.SendSignal(PtySignal.Interrupt, PtyProcessTarget.PrimaryProcess));
	}
	[Fact]
	public async Task Exited_primary_is_unavailable_without_claiming_scope_exit() {
		PtyStartInfo info = PtyTestSupport.Child("exit"); info.Ownership = PtyProcessOwnership.PlatformScope;
		using PtyProcess process = await PtyProcess.StartAsync(info); Task<string> drain = PtyTestSupport.Drain(process.Output);
		Assert.Equal(37, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10))); await drain;
		Assert.Equal(new(PtyProcessTarget.PrimaryProcess, PtyControlStatus.TargetUnavailable), process.RequestTermination(PtyProcessTarget.PrimaryProcess));
		Assert.True(process.Capabilities.HasFlag(PtyProcessCapabilities.TerminateOwnedScope));
	}
	[Theory]
	[InlineData(PtySignal.Hangup, 129)] [InlineData(PtySignal.Interrupt, 130)] [InlineData(PtySignal.Terminate, 143)] [InlineData(PtySignal.Kill, 137)]
	public async Task Native_signals_reach_primary(PtySignal signal, int code) {
		if (OperatingSystem.IsWindows()) return;
		PtyStartInfo info = new("/bin/sh") { Ownership = PtyProcessOwnership.PlatformScope }; info.ArgumentList.Add("-c"); info.ArgumentList.Add("printf 'SIGNAL-READY'; exec sleep 20");
		using PtyProcess process = await PtyProcess.StartAsync(info); await PtyTestSupport.ReadUntil(process.Output, "SIGNAL-READY");
		Task<string> drain = PtyTestSupport.Drain(process.Output);
		Assert.Equal(PtyControlStatus.Requested, process.SendSignal(signal, PtyProcessTarget.PrimaryProcess).Status);
		Assert.Equal(code, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10))); await drain;
	}
}
