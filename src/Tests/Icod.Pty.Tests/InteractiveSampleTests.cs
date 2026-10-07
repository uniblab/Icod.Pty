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
	public async Task Native_host_output_backpressure_does_not_prevent_cleanup() {
		// This reproduces a Linux native write stalled in Console.OpenStandardOutput.
		// Other native lifetimes are covered by the common stalled-output probe.
		if (!OperatingSystem.IsLinux()) return;
		string marker = Path.Combine(Path.GetTempPath(), "icod-pty-backpressure-" + Guid.NewGuid().ToString("N"));
		try {
			await using PtyProcess outer = await PtyProcess.StartAsync(Sample("backpressure-exit", marker));
			Task? drain = null;
			try {
				using CancellationTokenSource startup = new(TimeSpan.FromSeconds(10));
				while (!File.Exists(marker)) await Task.Delay(10, startup.Token);
				// Deliberately do not read outer.Output until the sample has stopped.
				Assert.Equal(1, await outer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)));
			} finally {
				drain = outer.Output.CopyToAsync(Stream.Null);
				await outer.DisposeAsync();
				try { await drain.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception error) when (error is ObjectDisposedException or IOException) { }
			}
		} finally { File.Delete(marker); }
	}
	[Fact]
	public async Task Sample_restores_host_after_start_failure() => await Probe("start-failure", 1);
	[Fact]
	public async Task Sample_restores_host_after_child_exit() => await Probe("child-exit", 37);
	[Fact]
	public async Task Sample_handles_native_terminal_lifetime_after_primary_exit() => await Probe("retained-terminal", OperatingSystem.IsLinux() ? 1 : 0);
	[Fact]
	public async Task Sample_reports_drain_timeout_and_restores_host() => await Probe("drain-timeout", 1);
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
			Assert.True(report.Contains("SAMPLE-EXIT:" + expectedCode, StringComparison.Ordinal), report);
			Assert.True(report.Contains("RESTORED:True", StringComparison.Ordinal), report);
			if (scenario == "drain-timeout" || (scenario == "retained-terminal" && OperatingSystem.IsLinux())) Assert.Contains("Output draining timed out", report);
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
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public Task Native_terminal_preserves_split_keys_and_complete_query_reply(bool nested) => VerifyNativeInput(nested, false);
	[UnixSplitInputTheory]
	[InlineData(false)]
	[InlineData(true)]
	public Task Native_terminal_preserves_split_query_reply(bool nested) => VerifyNativeInput(nested, true);
	[WindowsConPtyProbeFact]
	public async Task ConPty_fragmentation_probe_writes_classified_report() {
		string? reportPath = Environment.GetEnvironmentVariable("ICOD_PTY_CONPTY_REPORT_PATH");
		if (string.IsNullOrWhiteSpace(reportPath))
			throw new InvalidOperationException("ICOD_PTY_CONPTY_REPORT_PATH must identify an existing output directory.");
		using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(5));

		ConPtyProbeReport report = await ConPtyFragmentationProbe.RunAsync(5, deadline.Token);
		ConPtyFragmentationProbe.WriteReport(reportPath, report);

		Assert.NotEqual(ConPtyProbeOutcome.Unavailable, report.Outcome);
		Assert.Equal(100, report.Trials.Count);
		Assert.Equal(2, report.Trials.Select(trial => trial.HostPath).Distinct().Count());
		Assert.Equal(10, report.Trials.Select(trial => trial.Pattern).Distinct().Count());
		Assert.Equal(5, report.Trials.Max(trial => trial.Attempt));
		Assert.Contains(report.Outcome, new[] {
			ConPtyProbeOutcome.Reproduced,
			ConPtyProbeOutcome.NotReproduced,
			ConPtyProbeOutcome.Inconclusive
		});
	}
	[Fact]
	public async Task Sample_input_pump_preserves_arbitrary_chunks() {
		await using PtyProcess process = await PtyProcess.StartAsync(PtyTestSupport.Child("forward-chunks"));
		Task<string> output = PtyTestSupport.Drain(process.Output);
		Assert.Equal(0, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)));
		Assert.Contains("FORWARDED:E99BAA1B5B411B5B31323B333452", await output);
	}
	private static async Task VerifyNativeInput(bool nested, bool splitQuery) {
		byte[] bytes = Encoding.UTF8.GetBytes("雪\u001b[A\u001b[12;34R");
		string trace = Path.Combine(Path.GetTempPath(), "icod-pty-raw-" + Guid.NewGuid().ToString("N"));
		try {
			for (int attempt = 0; attempt < 5; attempt++) {
				string[] arguments = ["raw-sequence", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), trace, "4096"];
				await using PtyProcess outer = await PtyProcess.StartAsync(nested ? Sample(arguments) : PtyTestSupport.Child(arguments));
				try {
					await PtyTestSupport.ReadUntil(outer.Output, "RAW-READY");
					// Always split UTF-8 and arrow-key input. Native ConPTY can discard
					// fragmented query prefixes, independently of the forwarding sample.
					int splitLength = splitQuery ? bytes.Length : 6;
					foreach (byte value in bytes.AsSpan(0, splitLength).ToArray()) await outer.Input.WriteAsync(new[] { value });
					if (!splitQuery) await outer.Input.WriteAsync(bytes.AsMemory(splitLength));
					await PtyTestSupport.ReadUntil(outer.Output, "SEQUENCE:" + Convert.ToHexString(bytes));
					Assert.Equal(bytes, ReadTrace(trace));
					await outer.Input.WriteAsync(new byte[] { 4 });
					Task<string> drain = PtyTestSupport.Drain(outer.Output);
					Assert.Equal(23, await outer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20))); await drain;
				} catch (Exception error) {
					string received = File.Exists(trace) ? Convert.ToHexString(ReadTrace(trace)) : "<no trace>";
					throw new IOException($"Raw input attempt {attempt}, nested={nested}, splitQuery={splitQuery}, exited={outer.HasExited}, received={received}.", error);
				}
			}
		} finally { File.Delete(trace); }
	}
	private static byte[] ReadTrace(string path) {
		using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using MemoryStream bytes = new(); file.CopyTo(bytes); return bytes.ToArray();
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
		string text = await PtyTestSupport.ReadUntil(outer.Output, "READY:");
		text += await PtyTestSupport.ReadUntil(outer.Output, "\n");
		Assert.Contains("argument with spaces", text); Assert.Contains("snow-\\u96EA", text); Assert.Contains("ends\\\\", text);
	}
}

internal sealed class UnixSplitInputTheoryAttribute : TheoryAttribute {
	public UnixSplitInputTheoryAttribute() {
		if (OperatingSystem.IsWindows())
			Skip = "Native ConPTY can discard fragmented CSI query-reply prefixes; see docs/ConPTY-Input-Limitations.md. The sample pump is tested separately on Windows.";
	}
}

internal sealed class WindowsConPtyProbeFactAttribute : FactAttribute {
	public WindowsConPtyProbeFactAttribute() {
		if (!OperatingSystem.IsWindows())
			Skip = "The ConPTY fragmentation classifier runs only on Windows.";
		else if (Environment.GetEnvironmentVariable("ICOD_PTY_VERIFY_SPLIT_QUERIES") != "1")
			Skip = "Set ICOD_PTY_VERIFY_SPLIT_QUERIES=1 to run the bounded ConPTY fragmentation classifier.";
	}
}
