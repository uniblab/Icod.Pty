using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyOwnershipTests {
	[Fact]
	public void Ownership_defaults_to_primary() {
		PtyStartInfo info = PtyTestSupport.Child();
		Assert.Equal(PtyProcessOwnership.PrimaryProcess, info.Ownership);
		Assert.Equal(PtyProcessOwnership.PrimaryProcess, LaunchConfiguration.Capture(info).Ownership);
	}
	[Fact]
	public async Task Ownership_is_snapshotted_before_yield() {
		PtyStartInfo info = PtyTestSupport.Child(); info.Ownership = PtyProcessOwnership.PlatformScope;
		LaunchConfiguration captured = LaunchConfiguration.Capture(info);
		TaskCompletionSource<IPtyBackend> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
		LaunchConfiguration? seen = null;
		Task<PtyProcess> pending = PtyProcess.StartCoreAsync(captured, default, (launch, _) => { seen = launch; return ready.Task; });
		info.Ownership = PtyProcessOwnership.PrimaryProcess;
		Assert.Equal(PtyProcessOwnership.PlatformScope, seen!.Ownership);
		ready.SetResult(new ControlledBackend { Ownership = seen.Ownership });
		using PtyProcess process = await pending;
		Assert.Equal(PtyProcessOwnership.PlatformScope, process.Ownership);
	}
	[Fact]
	public void Invalid_ownership_does_not_create_child() {
		PtyStartInfo info = PtyTestSupport.Child("exit"); info.Ownership = (PtyProcessOwnership)1234;
		Assert.Throws<ArgumentOutOfRangeException>(() => { _ = PtyProcess.StartAsync(info); });
	}
	[Fact]
	public async Task Capabilities_do_not_claim_liveness() {
		ControlledBackend backend = new() { Ownership = PtyProcessOwnership.PlatformScope, Capabilities = PtyProcessCapabilities.TerminateOwnedScope };
		using PtyProcess process = await backend.Start();
		Assert.Equal(PtyProcessCapabilities.TerminateOwnedScope, process.Capabilities);
		backend.Completion.TrySetResult(37); Assert.Equal(37, await process.WaitForExitAsync());
		process.Dispose();
		Assert.Equal(PtyProcessOwnership.PlatformScope, process.Ownership);
		Assert.Equal(PtyProcessCapabilities.TerminateOwnedScope, process.Capabilities);
	}
	[Fact]
	public async Task Legacy_terminate_remains_primary_only() {
		ControlledBackend backend = new() { Ownership = PtyProcessOwnership.PlatformScope };
		using PtyProcess process = await backend.Start(); process.Terminate();
		Assert.Equal(1, backend.TerminateCount); Assert.Equal(0, backend.ScopeTerminationCount);
	}
}
