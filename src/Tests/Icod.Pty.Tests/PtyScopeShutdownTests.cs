using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyScopeShutdownTests {
	[Fact]
	public async Task Owned_shutdown_records_target_and_snapshot() {
		ControlledBackend backend = new() { Ownership = PtyProcessOwnership.PlatformScope }; using PtyProcess process = await backend.Start();
		PtyShutdownOptions options = new() { ForceTermination = true, TerminationTarget = PtyProcessTarget.OwnedScope, GracePeriod = TimeSpan.FromMilliseconds(20), TerminationTimeout = TimeSpan.FromMilliseconds(20) };
		Task<PtyShutdownResult> pending = process.ShutdownAsync(options); options.TerminationTarget = PtyProcessTarget.PrimaryProcess;
		PtyShutdownResult result = await pending;
		Assert.Equal(new PtyControlResult(PtyProcessTarget.OwnedScope, PtyControlStatus.Requested), result.TerminationResult);
		Assert.Equal(1, backend.ScopeTerminationCount); Assert.Equal(0, backend.TerminateCount); Assert.True(result.ForcedTerminationRequested);
	}
	[Fact]
	public async Task Unsupported_target_is_rejected_before_input() {
		ControlledBackend backend = new(); using PtyProcess process = await backend.Start();
		await Assert.ThrowsAsync<InvalidOperationException>(() => process.ShutdownAsync(new() { TerminationTarget = PtyProcessTarget.OwnedScope, Request = new byte[] { 1 } }));
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => process.ShutdownAsync(new() { TerminationTarget = (PtyProcessTarget)99, Request = new byte[] { 1 } }));
		Assert.Equal(0, backend.Input.Length);
	}
	[Fact]
	public async Task Graceful_primary_exit_does_not_dispatch_scope_control() {
		ControlledBackend backend = new() { Ownership = PtyProcessOwnership.PlatformScope }; using PtyProcess process = await backend.Start(); backend.Completion.SetResult(37);
		PtyShutdownResult result = await process.ShutdownAsync(new() { TerminationTarget = PtyProcessTarget.OwnedScope, ForceTermination = true });
		Assert.Null(result.TerminationResult); Assert.False(result.ForcedTerminationRequested); Assert.Equal(0, backend.ScopeTerminationCount);
	}
	[Fact]
	public void Old_result_constructor_and_deconstruction_work() {
		PtyShutdownResult result = new(PtyShutdownStatus.Exited, 37, false); var (status, code, forced) = result;
		Assert.Equal(PtyShutdownStatus.Exited, status); Assert.Equal(37, code); Assert.False(forced); Assert.Null(result.TerminationResult);
		Assert.Equal(PtyProcessTarget.PrimaryProcess, new PtyShutdownOptions().TerminationTarget);
	}
}
