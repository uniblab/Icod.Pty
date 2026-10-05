using Icod.Pty.Session;
using Icod.Pty.Unix;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class TerminalConfigurationIntegrationTests {
	[Fact]
	public async Task Child_first_state_matches_request() {
		if (OperatingSystem.IsWindows()) return;
		PtyStartInfo info = PtyTestSupport.Child("terminal-config-state"); info.TerminalOptions = new() {
			Echo = false, CanonicalInput = false, SignalProcessing = false,
			InterruptCharacter = 28, EndOfFileCharacter = 5, EraseCharacter = 8, MinimumReadBytes = 2, ReadTimeoutDeciseconds = 1
		};
		await using PtyProcess process = await PtyProcess.StartAsync(info);
		string output = await PtyTestSupport.Drain(process.Output); Assert.Equal(0, await process.WaitForExitAsync());
		using JsonDocument report = JsonDocument.Parse(output.Replace("\r", "", StringComparison.Ordinal).Trim());
		JsonElement state = report.RootElement;
		Assert.False(state.GetProperty("Echo").GetBoolean()); Assert.False(state.GetProperty("CanonicalInput").GetBoolean()); Assert.False(state.GetProperty("SignalProcessing").GetBoolean());
		Assert.Equal(28, state.GetProperty("InterruptCharacter").GetByte()); Assert.Equal(5, state.GetProperty("EndOfFileCharacter").GetByte()); Assert.Equal(8, state.GetProperty("EraseCharacter").GetByte());
		Assert.Equal(2, state.GetProperty("MinimumReadBytes").GetByte()); Assert.Equal(1, state.GetProperty("ReadTimeoutDeciseconds").GetByte());
	}

	[Fact]
	public async Task Canonical_waits_for_delimiter() {
		if (OperatingSystem.IsWindows()) return;
		await using PtyProcess process = await StartReader(new());
		await PtyTestSupport.ReadUntil(process.Output, "READ-READY");
		Task<string> read = PtyTestSupport.ReadUntil(process.Output, ":END");
		await process.Input.WriteAsync(Encoding.ASCII.GetBytes("ABC"));
		await Task.Delay(250); Assert.False(read.IsCompleted);
		await process.Input.WriteAsync(new byte[] { 10 });
		Assert.Contains("READ:4:4142430A", await read.WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.Equal(0, await process.WaitForExitAsync());
	}

	[Fact]
	public async Task Noncanonical_reads_without_delimiter() {
		if (OperatingSystem.IsWindows()) return;
		await using PtyProcess process = await StartReader(new() { Echo = false, CanonicalInput = false, MinimumReadBytes = 1, ReadTimeoutDeciseconds = 0 });
		await PtyTestSupport.ReadUntil(process.Output, "READ-READY");
		await process.Input.WriteAsync(new byte[] { 0x41 });
		Assert.Contains("READ:1:41:END", await PtyTestSupport.ReadUntil(process.Output, ":END"));
	}

	[Fact]
	public async Task Echo_off_emits_no_input_echo() {
		if (OperatingSystem.IsWindows()) return;
		await using PtyProcess process = await StartReader(new() { Echo = false, CanonicalInput = false, MinimumReadBytes = 1, ReadTimeoutDeciseconds = 0 });
		await PtyTestSupport.ReadUntil(process.Output, "READ-READY");
		await process.Input.WriteAsync(new byte[] { (byte)'Z' });
		string output = await PtyTestSupport.ReadUntil(process.Output, ":END");
		Assert.DoesNotContain("Z", output); Assert.Contains("READ:1:5A", output);
	}

	[Fact]
	public async Task Custom_control_characters_take_effect() {
		if (OperatingSystem.IsWindows()) return;
		await using PtyProcess process = await StartReader(new() { Echo = false, CanonicalInput = false, SignalProcessing = true, InterruptCharacter = 28, MinimumReadBytes = 1, ReadTimeoutDeciseconds = 0 });
		await PtyTestSupport.ReadUntil(process.Output, "READ-READY");
		await process.SendInterruptAsync();
		Assert.Contains("READ:1:03:END", await PtyTestSupport.ReadUntil(process.Output, ":END"));
		Assert.Equal(0, await process.WaitForExitAsync());
	}

	[Fact]
	public async Task Raw_ETX_is_data() {
		if (OperatingSystem.IsWindows()) return;
		await using PtyProcess process = await StartReader(new() { Profile = PtyTerminalProfile.Raw });
		await PtyTestSupport.ReadUntil(process.Output, "READ-READY");
		await process.SendInterruptAsync();
		Assert.Contains("READ:1:03:END", await PtyTestSupport.ReadUntil(process.Output, ":END"));
	}

	[Fact]
	public async Task Child_may_change_initial_configuration() {
		if (OperatingSystem.IsWindows()) return;
		PtyStartInfo info = PtyTestSupport.Child("terminal-config-change"); info.TerminalOptions = new() { Echo = true, CanonicalInput = true };
		await using PtyProcess process = await PtyProcess.StartAsync(info);
		string output = await PtyTestSupport.Drain(process.Output); Assert.Equal(0, await process.WaitForExitAsync());
		string changedLine = output.Replace("\r", "", StringComparison.Ordinal).Split('\n').Single(line => line.StartsWith("CHANGED:", StringComparison.Ordinal));
		using JsonDocument changed = JsonDocument.Parse(changedLine[8..]);
		Assert.False(changed.RootElement.GetProperty("Echo").GetBoolean()); Assert.False(changed.RootElement.GetProperty("CanonicalInput").GetBoolean());
	}

	[Theory]
	[InlineData(0, 0, false, 0)]
	[InlineData(0, 1, false, 0)]
	[InlineData(1, 0, true, 1)]
	[InlineData(2, 1, true, 1)]
	public async Task Read_timing_combinations(int minimum, int timeout, bool sendByte, int expectedCount) {
		if (OperatingSystem.IsWindows()) return;
		await using PtyProcess process = await StartReader(new() { Echo = false, CanonicalInput = false, MinimumReadBytes = minimum, ReadTimeoutDeciseconds = timeout });
		await PtyTestSupport.ReadUntil(process.Output, "READ-READY");
		if (sendByte) await process.Input.WriteAsync(new byte[] { 0x41 });
		Assert.Contains($"READ:{expectedCount}:", await PtyTestSupport.ReadUntil(process.Output, ":END").WaitAsync(TimeSpan.FromSeconds(10)));
	}
	[Theory]
	[InlineData(PtyProcessOwnership.PrimaryProcess)]
	[InlineData(PtyProcessOwnership.PlatformScope)]
	public async Task Configuration_precedes_both_launch_paths(PtyProcessOwnership ownership) {
		if (OperatingSystem.IsWindows()) return;
		List<string> operations = [];
		PtyStartInfo info = PtyTestSupport.Child("exit"); info.Ownership = ownership; info.TerminalOptions = new() { Echo = false };
		LaunchConfiguration launch = LaunchConfiguration.Capture(info);
		RecordingOperations native = new(operations);
		using IPtyBackend backend = await UnixBackend.StartAsync(launch, default, native, () => {
			operations.Add("launch");
			Assert.Contains("readback", operations);
		});
		Assert.Equal(37, await backend.Exit.WaitAsync(TimeSpan.FromSeconds(20)));
		Assert.True(operations.IndexOf("set") < operations.IndexOf("readback"));
		Assert.True(operations.IndexOf("readback") < operations.IndexOf("launch"));
	}

	[Fact]
	public async Task Configuration_failure_never_launches_child() {
		if (OperatingSystem.IsWindows()) return;
		string marker = Path.Combine(Path.GetTempPath(), "icod-pty-terminal-" + Guid.NewGuid().ToString("N"));
		PtyStartInfo info = PtyTestSupport.Child("write-marker", marker); info.TerminalOptions = new() { Echo = false };
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
			PtyStartInfo info = PtyTestSupport.Child("exit"); info.TerminalOptions = new() { Echo = false };
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UnixBackend.StartAsync(LaunchConfiguration.Capture(info), cancellation.Token, native));
		}
		Assert.True(DescriptorCount() <= baseline + 1, "Repeated cancelled configuration attempts leaked descriptors.");
	}

	[Fact]
	public async Task Session_configuration_failure_leaves_streams_open() {
		if (OperatingSystem.IsWindows()) return;
		TrackingStream input = new(), output = new();
		PtyStartInfo info = PtyTestSupport.Child("exit"); info.TerminalOptions = new() { Echo = false };
		LaunchConfiguration launch = LaunchConfiguration.Capture(info);
		SessionConfiguration session = SessionConfiguration.Capture(new(output) { Input = input, LeaveInputOpen = false, LeaveOutputOpen = false });
		RecordingOperations native = new([]) { FailReadback = true };
		await Assert.ThrowsAsync<IOException>(() => PtySession.StartCoreAsync(launch, session, default,
			async (captured, token) => await PtyProcess.StartCoreAsync(captured, token, (candidate, inner) => UnixBackend.StartAsync(candidate, inner, native))));
		Assert.Equal(0, input.Disposals); Assert.Equal(0, output.Disposals);
		Assert.True(input.CanRead); Assert.True(output.CanWrite);
	}

	private static int DescriptorCount() => Directory.EnumerateFileSystemEntries(OperatingSystem.IsLinux() ? "/proc/self/fd" : "/dev/fd").Count();
	private static async Task<PtyProcess> StartReader(PtyTerminalOptions options) {
		PtyStartInfo info = PtyTestSupport.Child("terminal-config-read", "64"); info.TerminalOptions = options;
		return await PtyProcess.StartAsync(info);
	}

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
