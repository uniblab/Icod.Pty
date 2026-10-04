using System.ComponentModel;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PrimaryControlTests {
	[Fact]
	public void Native_exit_between_check_and_dispatch_is_unavailable() {
		bool exited = false;
		PtyControlResult result = PrimaryProcessControl.RequestNative(() => exited, () => { exited = true; return false; }, () => 5);
		Assert.Equal(PtyControlStatus.TargetUnavailable, result.Status);
	}
	[Fact]
	public void Native_success_and_native_denial_remain_distinct() {
		Assert.Equal(PtyControlStatus.Requested, PrimaryProcessControl.RequestNative(() => false, () => true, () => 0).Status);
		IOException failure = Assert.Throws<IOException>(() => PrimaryProcessControl.RequestNative(() => false, () => false, () => 5));
		Assert.Contains("PrimaryProcess", failure.Message); Assert.Contains("5", failure.Message);
	}
	[Fact]
	public void Managed_void_dispatch_cannot_claim_native_acceptance() {
		bool exited = false;
		PtyControlResult result = PrimaryProcessControl.RequestManaged(() => exited, () => exited = true);
		Assert.Equal(PtyControlStatus.DispatchUnconfirmed, result.Status);
		Assert.Equal(PtyControlStatus.DispatchUnconfirmed, PrimaryProcessControl.RequestManaged(() => false, () => { }).Status);
		Assert.Equal(PtyControlStatus.TargetUnavailable, PrimaryProcessControl.RequestManaged(() => true, () => throw new Exception()).Status);
	}
	[Fact]
	public void Managed_native_denial_has_context_and_original_error() {
		Win32Exception native = new(1);
		IOException failure = Assert.Throws<IOException>(() => PrimaryProcessControl.RequestManaged(() => false, () => throw native));
		Assert.Same(native, failure.InnerException); Assert.Contains("PrimaryProcess", failure.Message); Assert.Contains("1", failure.Message);
	}
}
