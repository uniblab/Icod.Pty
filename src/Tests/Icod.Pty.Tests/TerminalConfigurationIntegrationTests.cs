using Icod.Pty.Session;
using Icod.Pty.Unix;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class TerminalConfigurationIntegrationTests {
	[Theory]
	[InlineData(PtyProcessOwnership.PrimaryProcess)]
	[InlineData(PtyProcessOwnership.PlatformScope)]
	public async Task Configuration_precedes_both_launch_paths(PtyProcessOwnership ownership) {
		if (OperatingSystem.IsWindows()) return;
		List<string> operations = [];
		PtyStartInfo info = PtyTestSupport.Child("exit") { Ownership = ownership, TerminalOptions = new() { Echo = false } };
		LaunchConfiguration launch = LaunchConfiguration.Capture(info);
		RecordingOperations native = new(operations);
		using IPtyBackend backend = await UnixBackend.StartAsync(launch, default, native, () => {
			operations.Add("launch");
			Assert.Contains("readback", operations);
		});
		Assert.Equal(0, await backend.Exit.WaitAsync(TimeSpan.FromSeconds(20)));
		Assert.True(operations.IndexOf("set") < operations.IndexOf("readback"));
		Assert.True(operations.IndexOf("readback") < operations.IndexOf("launch"));
	}

	[Fact]
	public async Task Configuration_failure_never_launches_child() {
		if (OperatingSystem.IsWindows()) return;
		string marker = Path.Combine(Path.GetTempPath(), "icod-pty-terminal-" + Guid.NewGuid().ToString("N"));
		PtyStartInfo info = PtyTestSupport.Child("write-marker", marker) { TerminalOptions = new() { Echo = false } };
		RecordingOperations native = new([]) { FailSet = true };
		await Assert.ThrowsAsync<IOException>(() => UnixBackend.StartAsync(LaunchConfiguration.Capture(info), default, native, () => Assert.Fail("launch reached")));
		Assert.False(File.Exists(marker));
	}

	[Fact]
	public async Task Cancelled_configuration_releases_descriptors() {
		if (OperatingSystem.IsWindows()) return;
		int baseline = DescriptorCount();
		for (int attempt = 0; attempt < 8; attempt++) {
			using CancellationTokenSource cancellation = new();
			RecordingOperations native = new([]) { AfterSet = cancellation.Cancel };
			PtyStartInfo info = PtyTestSupport.Child("exit") { TerminalOptions = new() { Echo = false } };
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UnixBackend.StartAsync(LaunchConfiguration.Capture(info), cancellation.Token, native));
		}
		Assert.InRange(DescriptorCount(), baseline - 1, baseline + 1);
	}

	[Fact]
	public async Task Session_configuration_failure_leaves_streams_open() {
		if (OperatingSystem.IsWindows()) return;
		TrackingStream input = new(), output = new();
		PtyStartInfo info = PtyTestSupport.Child("exit") { TerminalOptions = new() { Echo = false } };
		LaunchConfiguration launch = LaunchConfiguration.Capture(info);
		SessionConfiguration session = SessionConfiguration.Capture(new(output) { Input = input, LeaveInputOpen = false, LeaveOutputOpen = false });
		RecordingOperations native = new([]) { FailReadback = true };
		await Assert.ThrowsAsync<IOException>(() => PtySession.StartCoreAsync(launch, session, default,
			async (captured, token) => await PtyProcess.StartCoreAsync(captured, token, (candidate, inner) => UnixBackend.StartAsync(candidate, inner, native))));
		Assert.False(input.Disposed); Assert.False(output.Disposed);
	}

	private static int DescriptorCount() => Directory.EnumerateFileSystemEntries(OperatingSystem.IsLinux() ? "/proc/self/fd" : "/dev/fd").Count();

	private sealed class RecordingOperations(List<string> operations) : IUnixTerminalOperations {
		private int gets;
		private UnixTerminalState current = Initial();
		internal bool FailSet { get; init; }
		internal bool FailReadback { get; init; }
		internal Action? AfterSet { get; init; }
		public int LastError { get; private set; }
		public int GetAttributes(int descriptor, out UnixTerminalState state) {
			gets++; operations.Add(gets == 1 ? "get" : "readback"); state = current.Clone();
			if (gets > 1 && FailReadback) { LastError = 5; return -1; }
			return 0;
		}
		public int SetAttributes(int descriptor, UnixTerminalState state) {
			operations.Add("set");
			if (FailSet) { LastError = 5; return -1; }
			current = state.Clone(); AfterSet?.Invoke(); return 0;
		}
		public int GetDisabledCharacter(int descriptor, out byte value) { value = 0; return 0; }
		public void MakeRaw(UnixTerminalState state) { }
		private static UnixTerminalState Initial() {
			UnixTerminalPlatform platform = OperatingSystem.IsMacOS() ? UnixTerminalPlatform.Darwin : UnixTerminalPlatform.Linux;
			UnixTerminalConstants constants = UnixTerminalConstants.For(platform);
			return new(platform, 0, 0, 0, constants.Echo | constants.EchoNewline | constants.Canonical | constants.SignalProcessing, 0, new byte[constants.ControlCount], 0, 0);
		}
	}
}
