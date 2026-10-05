using System.Diagnostics;
using System.Text.Json;
using Icod.Pty.Unix;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class UnixTerminalConfigurationTests {
	private const PtyTerminalCapabilities All = PtyTerminalCapabilities.RawProfile | PtyTerminalCapabilities.Echo | PtyTerminalCapabilities.CanonicalInput | PtyTerminalCapabilities.SignalProcessing | PtyTerminalCapabilities.ControlCharacters | PtyTerminalCapabilities.ReadTiming;

	[Fact]
	public void Preserve_changes_only_requested_fields() {
		UnixTerminalState original = State(UnixTerminalPlatform.Linux);
		FakeTerminalOperations native = new(original);
		TerminalConfiguration configuration = TerminalConfiguration.Capture(new() { CanonicalInput = false }, All)!;
		UnixTerminalConfiguration.Apply(42, configuration, native);
		UnixTerminalState written = Assert.IsType<UnixTerminalState>(native.Written);
		Assert.Equal(original.InputFlags, written.InputFlags); Assert.Equal(original.OutputFlags, written.OutputFlags); Assert.Equal(original.ControlFlags, written.ControlFlags);
		Assert.Equal(original.LocalFlags & ~UnixTerminalConstants.For(original.Platform).Canonical, written.LocalFlags);
		Assert.Equal(original.Line, written.Line); Assert.Equal(original.InputSpeed, written.InputSpeed); Assert.Equal(original.OutputSpeed, written.OutputSpeed);
		Assert.Equal(original.ControlCharacters, written.ControlCharacters);
	}

	[Fact]
	public void Echo_off_clears_newline_echo() {
		UnixTerminalState original = State(UnixTerminalPlatform.Darwin);
		UnixTerminalConstants constants = UnixTerminalConstants.For(original.Platform);
		original.LocalFlags |= constants.Echo | constants.EchoNewline | 0x400000;
		FakeTerminalOperations native = new(original);
		UnixTerminalConfiguration.Apply(42, TerminalConfiguration.Capture(new() { Echo = false }, All)!, native);
		Assert.Equal(original.LocalFlags & ~(constants.Echo | constants.EchoNewline), native.Written!.LocalFlags);
		Assert.NotEqual(0UL, native.Written.LocalFlags & 0x400000UL);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Raw_sets_verified_masks_and_read_timing(bool darwin) {
		UnixTerminalPlatform platform = darwin ? UnixTerminalPlatform.Darwin : UnixTerminalPlatform.Linux;
		UnixTerminalState original = State(platform); FakeTerminalOperations native = new(original);
		UnixTerminalConfiguration.Apply(42, TerminalConfiguration.Capture(new() { Profile = PtyTerminalProfile.Raw }, All)!, native);
		UnixTerminalConstants constants = UnixTerminalConstants.For(platform); UnixTerminalState written = native.Written!;
		Assert.Equal(constants.RawInput(original.InputFlags), written.InputFlags);
		Assert.Equal(original.OutputFlags & ~constants.OutputPostProcessing, written.OutputFlags);
		Assert.Equal(constants.RawControl(original.ControlFlags), written.ControlFlags);
		Assert.Equal(original.LocalFlags & ~constants.RawLocalClear, written.LocalFlags);
		Assert.Equal(1, written.ControlCharacters[constants.MinimumIndex]);
		Assert.Equal(0, written.ControlCharacters[constants.TimeoutIndex]);
	}

	[Fact]
	public void Disabled_character_is_not_a_literal_byte() {
		FakeTerminalOperations native = new(State(UnixTerminalPlatform.Linux)) { DisabledCharacter = 0 };
		TerminalConfiguration literal = TerminalConfiguration.Capture(new() { InterruptCharacter = 0 }, All)!;
		Assert.Throws<ArgumentException>(() => UnixTerminalConfiguration.Apply(42, literal, native));
		Assert.Equal(0, native.SetCalls);
		TerminalConfiguration disabled = TerminalConfiguration.Capture(new() { InterruptCharacter = PtyTerminalOptions.DisabledCharacter }, All)!;
		UnixTerminalConfiguration.Apply(42, disabled, native);
		Assert.Equal(0, native.Written!.ControlCharacters[UnixTerminalConstants.For(UnixTerminalPlatform.Linux).InterruptIndex]);
	}

	[Fact]
	public void Partial_native_success_fails_readback() {
		UnixTerminalState original = State(UnixTerminalPlatform.Linux); UnixTerminalConstants constants = UnixTerminalConstants.For(original.Platform);
		original.LocalFlags |= constants.Echo;
		FakeTerminalOperations native = new(original) { ApplySet = false };
		IOException error = Assert.Throws<IOException>(() => UnixTerminalConfiguration.Apply(42, TerminalConfiguration.Capture(new() { Echo = false }, All)!, native));
		Assert.Contains("readback", error.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(1, native.SetCalls);
	}

	[Theory]
	[InlineData(0, "tcgetattr before")]
	[InlineData(1, "tcsetattr")]
	[InlineData(2, "tcgetattr after")]
	public void Native_errors_identify_operation_and_preserve_error(int failure, string operation) {
		FakeTerminalOperations native = new(State(UnixTerminalPlatform.Linux));
		if (failure == 0) native.GetResults.Enqueue((-1, 5));
		else { native.GetResults.Enqueue((0, 0)); if (failure == 1) native.SetResult = (-1, 6); else native.GetResults.Enqueue((-1, 7)); }
		IOException error = Assert.Throws<IOException>(() => UnixTerminalConfiguration.Apply(42, TerminalConfiguration.Capture(new() { Echo = false }, All)!, native));
		Assert.Contains(operation, error.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(failure + 5, Assert.IsType<System.ComponentModel.Win32Exception>(error.InnerException).NativeErrorCode);
	}

	[Fact]
	public void Null_configuration_performs_no_native_calls() {
		FakeTerminalOperations native = new(State(UnixTerminalPlatform.Linux));
		UnixTerminalConfiguration.Apply(42, null, native);
		Assert.Equal(0, native.GetCalls); Assert.Equal(0, native.SetCalls); Assert.Equal(0, native.DisabledCalls);
	}

	[Fact]
	public async Task Native_probe_verifies_layout_apply_readback_and_child_first_state() {
		if (OperatingSystem.IsWindows()) return;
		using JsonDocument report = JsonDocument.Parse(await RunProbe());
		JsonElement root = report.RootElement;
		Assert.True(root.GetProperty("NativeTermios").GetBoolean());
		Assert.Equal(OperatingSystem.IsMacOS() ? "Darwin" : "Linux", root.GetProperty("Platform").GetString());
		Assert.Equal(OperatingSystem.IsMacOS() ? 72 : 60, root.GetProperty("TermiosSize").GetInt32());
		Assert.Equal(OperatingSystem.IsMacOS() ? 8 : 4, root.GetProperty("FlagSize").GetInt32());
		Assert.Equal(OperatingSystem.IsMacOS() ? 32 : 17, root.GetProperty("ControlOffset").GetInt32());
		Assert.Equal(OperatingSystem.IsMacOS() ? 20 : 32, root.GetProperty("ControlCount").GetInt32());
		Assert.Equal(OperatingSystem.IsMacOS() ? 56 : 52, root.GetProperty("InputSpeedOffset").GetInt32());
		Assert.Equal(OperatingSystem.IsMacOS() ? 64 : 56, root.GetProperty("OutputSpeedOffset").GetInt32());
		Assert.Equal(OperatingSystem.IsMacOS() ? 8 : 0, root.GetProperty("InterruptIndex").GetInt32());
		Assert.Equal(OperatingSystem.IsMacOS() ? 0 : 4, root.GetProperty("EndOfFileIndex").GetInt32());
		Assert.Equal(OperatingSystem.IsMacOS() ? 3 : 2, root.GetProperty("EraseIndex").GetInt32());
		Assert.Equal(OperatingSystem.IsMacOS() ? 16 : 6, root.GetProperty("MinimumIndex").GetInt32());
		Assert.Equal(OperatingSystem.IsMacOS() ? 17 : 5, root.GetProperty("TimeoutIndex").GetInt32());
		Assert.Equal(OperatingSystem.IsMacOS() ? 255 : 0, root.GetProperty("DisabledCharacter").GetInt32());
		foreach (string property in new[] { "PreserveUnchanged", "BaselineChildMatches", "ConfiguredReadbackMatches", "ConfiguredChildMatches", "RawTransformationMatches", "RawReadbackMatches", "RawChildMatches" }) Assert.True(root.GetProperty(property).GetBoolean(), property);
		JsonElement configured = root.GetProperty("Configured");
		Assert.False(configured.GetProperty("Echo").GetBoolean());
		Assert.False(configured.GetProperty("CanonicalInput").GetBoolean());
		Assert.True(configured.GetProperty("SignalProcessing").GetBoolean());
		Assert.Equal(0x1c, configured.GetProperty("InterruptCharacter").GetByte());
		Assert.Equal(0x05, configured.GetProperty("EndOfFileCharacter").GetByte());
		Assert.Equal(0x08, configured.GetProperty("EraseCharacter").GetByte());
		JsonElement raw = root.GetProperty("Raw");
		Assert.False(raw.GetProperty("Echo").GetBoolean());
		Assert.False(raw.GetProperty("CanonicalInput").GetBoolean());
		Assert.False(raw.GetProperty("SignalProcessing").GetBoolean());
		Assert.Equal(1, raw.GetProperty("MinimumReadBytes").GetByte());
		Assert.Equal(0, raw.GetProperty("ReadTimeoutDeciseconds").GetByte());
	}

	[Fact]
	public async Task Default_launch_preserves_baseline_for_both_ownership_policies() {
		if (OperatingSystem.IsWindows()) return;
		JsonElement primary = await ReadInitialState(PtyProcessOwnership.PrimaryProcess);
		JsonElement scope = await ReadInitialState(PtyProcessOwnership.PlatformScope);
		foreach (JsonElement state in new[] { primary, scope }) {
			Assert.True(state.GetProperty("Echo").GetBoolean());
			Assert.True(state.GetProperty("CanonicalInput").GetBoolean());
			Assert.True(state.GetProperty("SignalProcessing").GetBoolean());
		}
		foreach (string property in new[] { "InputFlags", "OutputFlags", "ControlFlags", "LocalFlags", "ControlCharacters", "InputSpeed", "OutputSpeed" }) Assert.Equal(primary.GetProperty(property).GetRawText(), scope.GetProperty(property).GetRawText());
	}

	[Fact]
	public async Task Windows_probe_confirms_no_native_termios_path() {
		if (!OperatingSystem.IsWindows()) return;
		using JsonDocument report = JsonDocument.Parse(await RunProbe());
		Assert.Equal("Windows", report.RootElement.GetProperty("Platform").GetString());
		Assert.False(report.RootElement.GetProperty("NativeTermios").GetBoolean());
		Assert.False(report.RootElement.GetProperty("HostConsoleMutation").GetBoolean());
	}

	private static async Task<JsonElement> ReadInitialState(PtyProcessOwnership ownership) {
		PtyStartInfo start = PtyTestSupport.Child("terminal-config-state"); start.Ownership = ownership;
		await using PtyProcess process = await PtyProcess.StartAsync(start);
		string output = await PtyTestSupport.Drain(process.Output);
		Assert.Equal(0, await process.WaitForExitAsync());
		using JsonDocument document = JsonDocument.Parse(output.Replace("\r", "", StringComparison.Ordinal).Trim());
		return document.RootElement.Clone();
	}

	private static async Task<string> RunProbe() {
		ProcessStartInfo start = new(PtyTestSupport.DotNet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
		start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "child", "Icod.Pty.TestChild.dll"));
		start.ArgumentList.Add("terminal-config-native-probe");
		using Process process = Process.Start(start)!;
		Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
		try {
			await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
			Assert.True(process.ExitCode == 0, await error + await output);
			return await output;
		} finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
	}

	private static UnixTerminalState State(UnixTerminalPlatform platform) {
		UnixTerminalConstants constants = UnixTerminalConstants.For(platform);
		byte[] controls = Enumerable.Range(1, constants.ControlCount).Select(value => (byte)value).ToArray();
		return new(platform, 0xffff, 0xeeee, 0xdddd, 0xcccc | constants.Canonical | constants.SignalProcessing, platform == UnixTerminalPlatform.Linux ? (byte)7 : (byte)0, controls, 38400, 19200);
	}

	private sealed class FakeTerminalOperations : IUnixTerminalOperations {
		private UnixTerminalState current;
		internal Queue<(int Result, int Error)> GetResults { get; } = new();
		internal (int Result, int Error) SetResult { get; set; }
		internal byte DisabledCharacter { get; set; }
		internal bool ApplySet { get; set; } = true;
		internal int GetCalls { get; private set; }
		internal int SetCalls { get; private set; }
		internal int DisabledCalls { get; private set; }
		internal UnixTerminalState? Written { get; private set; }
		public int LastError { get; private set; }
		internal FakeTerminalOperations(UnixTerminalState state) { current = state.Clone(); DisabledCharacter = state.Platform == UnixTerminalPlatform.Darwin ? (byte)255 : (byte)0; }
		public int GetAttributes(int descriptor, out UnixTerminalState state) {
			GetCalls++; (int result, int error) = GetResults.Count == 0 ? (0, 0) : GetResults.Dequeue(); LastError = error; state = current.Clone(); return result;
		}
		public int SetAttributes(int descriptor, UnixTerminalState state) {
			SetCalls++; LastError = SetResult.Error; Written = state.Clone(); if (SetResult.Result == 0 && ApplySet) current = state.Clone(); return SetResult.Result;
		}
		public int GetDisabledCharacter(int descriptor, out byte value) { DisabledCalls++; value = DisabledCharacter; LastError = 0; return 0; }
		public void MakeRaw(UnixTerminalState state) {
			UnixTerminalConstants constants = UnixTerminalConstants.For(state.Platform);
			state.InputFlags = constants.RawInput(state.InputFlags); state.OutputFlags &= ~constants.OutputPostProcessing;
			state.ControlFlags = constants.RawControl(state.ControlFlags); state.LocalFlags &= ~constants.RawLocalClear;
			state.ControlCharacters[constants.MinimumIndex] = 1; state.ControlCharacters[constants.TimeoutIndex] = 0;
		}
	}
}
