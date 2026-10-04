using System.Diagnostics;
using System.Runtime.InteropServices;
using Icod.Pty.Windows;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class WindowsOwnershipTests {
	[Fact]
	public async Task Owned_child_is_assigned_before_resume() {
		if (!OperatingSystem.IsWindows()) return;
		using Fixture fixture = new(); WindowsJob? assigned = null;
		using IPtyBackend backend = await WindowsBackend.StartAsync(fixture.Launch(), default,
			process => { assigned = WindowsJob.CreateAssigned(process); Assert.True(WindowsNative.IsProcessInJob(process, assigned.Handle, out bool member) && member); return assigned; },
			thread => { Assert.NotNull(assigned); Assert.False(File.Exists(fixture.ParentReady)); return WindowsNative.ResumeThread(thread); });
		Task drain = backend.Output.CopyToAsync(Stream.Null);
		await fixture.WaitReady();
		Assert.Equal(PtyProcessOwnership.PlatformScope, backend.Ownership);
		Assert.Equal(PtyProcessCapabilities.TerminateOwnedScope, backend.Capabilities);
		backend.RequestTermination(PtyProcessTarget.OwnedScope);
		await backend.Exit.WaitAsync(TimeSpan.FromSeconds(10));
		await drain.WaitAsync(TimeSpan.FromSeconds(10));
	}
	[Fact]
	public async Task Job_survives_primary_exit() {
		if (!OperatingSystem.IsWindows()) return;
		using Fixture fixture = new();
		using IPtyBackend backend = await WindowsBackend.StartAsync(fixture.Launch(), default);
		Task drain = backend.Output.CopyToAsync(Stream.Null);
		await fixture.WaitReady();
		using Process descendant = Process.GetProcessById(fixture.ChildId()); _ = descendant.SafeHandle;
		File.WriteAllText(fixture.ExitPrimary, "exit");
		Assert.Equal(37, await backend.Exit.WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.False(descendant.HasExited);
		Assert.Equal(PtyControlStatus.Requested, backend.RequestTermination(PtyProcessTarget.OwnedScope).Status);
		await descendant.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
		await drain.WaitAsync(TimeSpan.FromSeconds(10));
	}
	[Theory]
	[InlineData(false)] [InlineData(true)]
	public async Task Launch_failure_collects_suspended_child(bool failResume) {
		if (!OperatingSystem.IsWindows()) return;
		using Fixture fixture = new(); Process? observer = null;
		try {
			IOException failure = await Assert.ThrowsAsync<IOException>(() => WindowsBackend.StartAsync(fixture.Launch(), default,
				process => {
					observer = Process.GetProcessById(checked((int)WindowsNative.GetProcessId(process))); _ = observer.SafeHandle;
					if (!failResume) throw new IOException("assignment failure");
					return WindowsJob.CreateAssigned(process);
				}, _ => WindowsNative.ResumeThread(0)));
			Assert.Contains(failResume ? "ResumeThread" : "assignment failure", failure.Message);
			Assert.NotNull(observer); await observer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
			Assert.False(File.Exists(fixture.ParentReady));
		} finally { observer?.Dispose(); }
	}
	[Fact]
	public async Task Cancellation_before_transfer_cleans_job() {
		if (!OperatingSystem.IsWindows()) return;
		using Fixture fixture = new(); using CancellationTokenSource cancel = new(); Process? observer = null;
		try {
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PtyProcess.StartCoreAsync(fixture.Launch(), cancel.Token,
				(launch, token) => WindowsBackend.StartAsync(launch, token,
					process => { observer = Process.GetProcessById(checked((int)WindowsNative.GetProcessId(process))); _ = observer.SafeHandle; return WindowsJob.CreateAssigned(process); },
					thread => { cancel.Cancel(); return WindowsNative.ResumeThread(thread); })));
			Assert.NotNull(observer); await observer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
		} finally { observer?.Dispose(); }
	}
	[Fact]
	public async Task Nested_job_failure_is_explicit() {
		if (!OperatingSystem.IsWindows()) return;
		using Fixture fixture = new();
		IOException failure = await Assert.ThrowsAsync<IOException>(() => WindowsBackend.StartAsync(fixture.Launch(), default,
			process => {
				using WindowsJob outer = WindowsJob.CreateAssigned(process);
				using WindowsJob restricted = new();
				WindowsNative.ExtendedLimitInformation limits = new() { Basic = new() { LimitFlags = 0x2008, ActiveProcesses = 0 } };
				Assert.True(WindowsNative.SetInformationJobObject(restricted.Handle, 9, ref limits, (uint)Marshal.SizeOf<WindowsNative.ExtendedLimitInformation>()));
				restricted.Assign(process); throw new InvalidOperationException("Restricted nested assignment unexpectedly succeeded.");
			}, WindowsNative.ResumeThread));
		Assert.Contains("AssignProcessToJobObject", failure.Message);
		Assert.False(File.Exists(fixture.ParentReady));
	}
	private sealed class Fixture : IDisposable {
		private readonly string directory = Path.Combine(Path.GetTempPath(), "icod-owned-" + Guid.NewGuid().ToString("N"));
		internal string ParentReady => Path.Combine(directory, "parent-ready");
		internal string ExitPrimary => Path.Combine(directory, "exit-primary");
		internal Fixture() { Directory.CreateDirectory(directory); }
		internal LaunchConfiguration Launch() {
			PtyStartInfo info = PtyTestSupport.Child("scope-windows-parent", directory); info.Ownership = PtyProcessOwnership.PlatformScope;
			return LaunchConfiguration.Capture(info);
		}
		internal async Task WaitReady() {
			using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
			while (!File.Exists(Path.Combine(directory, "child-ready"))) await Task.Delay(10, timeout.Token);
		}
		internal int ChildId() => int.Parse(File.ReadAllText(Path.Combine(directory, "child-ready")), System.Globalization.CultureInfo.InvariantCulture);
		public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
	}
}
