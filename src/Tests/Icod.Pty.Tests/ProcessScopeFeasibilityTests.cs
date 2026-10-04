using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class ProcessScopeFeasibilityTests {
	[Fact]
	public async Task Unix_managed_helper_exec_protocol_works_with_native_spawn() {
		if (OperatingSystem.IsWindows()) return;
		using JsonDocument result = JsonDocument.Parse(await RunProbe("scope-helper-probe", "ordinary"));
		Assert.Equal("1", result.RootElement.GetProperty("Handshake").GetString());
		Assert.Equal(37, result.RootElement.GetProperty("ExitCode").GetInt32());
		Assert.True(result.RootElement.GetProperty("WaitIdentityRetained").GetBoolean());
	}
	[Fact]
	public async Task Unix_ignored_sigchld_discards_native_wait_ownership() {
		if (OperatingSystem.IsWindows()) return;
		using JsonDocument result = JsonDocument.Parse(await RunProbe("scope-auto-reap-probe", "ignored"));
		Assert.True(result.RootElement.GetProperty("AutoReaped").GetBoolean());
		Assert.True(result.RootElement.GetProperty("IgnoredSignalDetected").GetBoolean());
	}
	[Theory]
	[InlineData("ordinary")]
	[InlineData("nested")]
	[InlineData("assignment-failure")]
	[InlineData("resume-failure")]
	public async Task Windows_scope_is_assigned_before_application_code(string scenario) {
		if (!OperatingSystem.IsWindows()) return;
		string output = await RunProbe("scope-windows-probe", scenario);
		using JsonDocument result = JsonDocument.Parse(output);
		Assert.True(result.RootElement.GetProperty("SuspendedBeforeAssignment").GetBoolean());
		Assert.True(result.RootElement.GetProperty("CleanupConfirmed").GetBoolean());
		Assert.Equal(scenario is "ordinary" or "nested", result.RootElement.GetProperty("ChildRan").GetBoolean());
	}
	private static async Task<string> RunProbe(string mode, string scenario) {
		ProcessStartInfo start = new(PtyTestSupport.DotNet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
		start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "child", "Icod.Pty.TestChild.dll"));
		start.ArgumentList.Add(mode); start.ArgumentList.Add(scenario);
		using Process process = Process.Start(start)!;
		Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
		try {
			await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
			Assert.True(process.ExitCode == 0, await error + await output);
			return await output;
		} finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
	}
	[Theory]
	[InlineData("ordinary")]
	[InlineData("reaper")]
	public async Task Unix_native_child_retains_wait_ownership_until_explicit_reap(string scenario) {
		if (OperatingSystem.IsWindows()) return;
		ProcessStartInfo start = new(PtyTestSupport.DotNet) {
			RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
		};
		start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "child", "Icod.Pty.TestChild.dll"));
		start.ArgumentList.Add("scope-native-probe"); start.ArgumentList.Add(scenario);
		using Process process = Process.Start(start)!;
		Task<string> output = process.StandardOutput.ReadToEndAsync();
		Task<string> error = process.StandardError.ReadToEndAsync();
		try {
			await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
			Assert.True(process.ExitCode == 0, await error + await output);
			using JsonDocument result = JsonDocument.Parse(await output);
			Assert.Equal(37, result.RootElement.GetProperty("ExitCode").GetInt32());
			Assert.True(result.RootElement.GetProperty("Retained").GetBoolean());
			Assert.True(result.RootElement.GetProperty("ManagedChildCollected").GetBoolean());
			Assert.True(result.RootElement.GetProperty("ReapedOnce").GetBoolean());
			Assert.Equal(scenario == "reaper", result.RootElement.GetProperty("InterferenceDetected").GetBoolean());
		} finally {
			if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
		}
	}
}
