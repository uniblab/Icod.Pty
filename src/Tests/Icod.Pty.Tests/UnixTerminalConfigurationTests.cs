using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class UnixTerminalConfigurationTests {
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
}
