using System.Diagnostics;
using System.Text;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class InteractiveSampleTests {
	internal static PtyStartInfo Sample(params string[] childArguments) {
		PtyStartInfo info = new(PtyTestSupport.DotNet) { DotNetHostPath = PtyTestSupport.DotNet, Size = new PtySize(100, 30) };
		info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "sample", "Icod.Pty.Sample.dll"));
		// Existing executable-and-arguments form must become immediately interactive.
		info.ArgumentList.Add(PtyTestSupport.DotNet);
		info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "child", "Icod.Pty.TestChild.dll"));
		foreach (string argument in childArguments) info.ArgumentList.Add(argument);
		return info;
	}
	[Fact]
	public async Task Sample_restores_host_after_start_failure() => await Probe("start-failure", 1);
	[Fact]
	public async Task Sample_restores_host_after_child_exit() => await Probe("child-exit", 37);
	[Fact]
	public async Task Sample_reports_drain_timeout_for_retained_terminal() => await Probe("retained-terminal", 1);
	[Theory]
	[InlineData("partial-setup")]
	[InlineData("cancel-before-read")]
	[InlineData("output-failure")]
	public async Task Sample_failure_paths_restore_host(string scenario) => await Probe(scenario, 1);
	private static async Task Probe(string scenario, int expectedCode) {
		string pidFile = Path.Combine(Path.GetTempPath(), "icod-pty-descendant-" + Guid.NewGuid().ToString("N"));
		try {
			PtyStartInfo info = PtyTestSupport.Child("host-console-probe", scenario,
				Path.Combine(AppContext.BaseDirectory, "sample", "Icod.Pty.Sample.dll"), pidFile);
			await using PtyProcess outer = await PtyProcess.StartAsync(info);
			string report = await PtyTestSupport.ReadUntil(outer.Output, "PROBE-READY");
			Assert.Contains("SAMPLE-EXIT:" + expectedCode, report); Assert.Contains("RESTORED:True", report);
			if (scenario == "retained-terminal") Assert.Contains("Output draining timed out", report);
			await outer.Input.WriteAsync(PtyTestSupport.Line("followup"));
			await PtyTestSupport.ReadUntil(outer.Output, "FOLLOWUP-ACK");
			Task<string> drain = PtyTestSupport.Drain(outer.Output);
			Assert.Equal(0, await outer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20))); await drain;
		} finally {
			if (File.Exists(pidFile)) {
				if (int.TryParse(File.ReadAllText(pidFile), out int id)) {
					try { using Process child = Process.GetProcessById(id); if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); } }
					catch (ArgumentException) { }
				}
				File.Delete(pidFile);
			}
		}
	}
	[Fact]
	public async Task Interactive_sample_forwards_single_key_without_enter() {
		await using PtyProcess outer = await PtyProcess.StartAsync(Sample("raw-input"));
		await PtyTestSupport.ReadUntil(outer.Output, "RAW-READY");
		await outer.Input.WriteAsync(new byte[] { 0x78 });
		string response = await PtyTestSupport.ReadUntil(outer.Output, "BYTE:78");
		Assert.DoesNotContain("x", response);
		await outer.Input.WriteAsync(new byte[] { 4 });
		Task<string> drain = PtyTestSupport.Drain(outer.Output);
		Assert.Equal(23, await outer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20))); await drain;
	}
	[Fact]
	public async Task Sample_preserves_utf8_vt_and_query_reply_chunks() {
		await using PtyProcess outer = await PtyProcess.StartAsync(Sample("raw-input"));
		await PtyTestSupport.ReadUntil(outer.Output, "RAW-READY");
		byte[] bytes = Encoding.UTF8.GetBytes("雪\u001b[A\u001b[12;34R");
		// Split inside a multibyte character and inside both escape sequences.
		foreach (byte value in bytes) await outer.Input.WriteAsync(new[] { value });
		foreach (byte value in bytes) await PtyTestSupport.ReadUntil(outer.Output, $"BYTE:{value:X2}");
		await outer.Input.WriteAsync(new byte[] { 4 });
		Task<string> drain = PtyTestSupport.Drain(outer.Output);
		Assert.Equal(23, await outer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20))); await drain;
	}
	[Fact]
	public async Task Sample_interrupt_reaches_child_not_host() {
		await using PtyProcess outer = await PtyProcess.StartAsync(Sample("interrupt-handler"));
		await PtyTestSupport.ReadUntil(outer.Output, "INTERRUPT-READY");
		await outer.SendInterruptAsync(); await PtyTestSupport.ReadUntil(outer.Output, "INTERRUPT-ACK");
		Assert.False(outer.HasExited);
		await outer.Input.WriteAsync(PtyTestSupport.Line("quit"));
		Task<string> drain = PtyTestSupport.Drain(outer.Output);
		Assert.Equal(23, await outer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)));
		Assert.Contains("BYE-MARKER", await drain);
	}
	[Fact]
	public async Task Sample_resize_reaches_inner_child() {
		await using PtyProcess outer = await PtyProcess.StartAsync(Sample());
		await PtyTestSupport.ReadUntil(outer.Output, "READY:ok");
		await ExpectSize(outer, "100,30");
		outer.Resize(new PtySize(117, 39)); await ExpectSize(outer, "117,39");
	}
	private static async Task ExpectSize(PtyProcess outer, string expected) {
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
		while (true) {
			string marker = "SIZE:" + Guid.NewGuid().ToString("N")[..8] + ":";
			await outer.Input.WriteAsync(PtyTestSupport.Line("size:" + marker[5..^1]), timeout.Token);
			string actual = await PtyTestSupport.ReadUntil(outer.Output, marker);
			actual += await PtyTestSupport.ReadUntil(outer.Output, "\n");
			if (actual.Contains(marker + expected, StringComparison.Ordinal)) return;
			await Task.Delay(50, timeout.Token);
		}
	}
	[Fact]
	public async Task Redirected_interactive_host_has_actionable_diagnostic() {
		using Process process = Process.Start(new ProcessStartInfo(PtyTestSupport.DotNet) {
			ArgumentList = { Path.Combine(AppContext.BaseDirectory, "sample", "Icod.Pty.Sample.dll"), "--interactive" },
			RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
		})!;
		try {
			Task<string> error = process.StandardError.ReadToEndAsync();
			await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
			Assert.Equal(1, process.ExitCode); Assert.Contains("--line", await error);
		} finally { if (!process.HasExited) process.Kill(true); }
	}
	[Fact]
	public async Task Explicit_interactive_delimiter_preserves_executable_arguments() {
		PtyStartInfo info = Sample("argument with spaces", "snow-雪", "ends\\");
		info.ArgumentList.Insert(1, "--interactive"); info.ArgumentList.Insert(2, "--");
		info.Size = new PtySize(1024, 31);
		await using PtyProcess outer = await PtyProcess.StartAsync(info);
		string text = await PtyTestSupport.ReadUntil(outer.Output, "\n");
		Assert.Contains("argument with spaces", text); Assert.Contains("snow-\\u96EA", text); Assert.Contains("ends\\\\", text);
	}
}
