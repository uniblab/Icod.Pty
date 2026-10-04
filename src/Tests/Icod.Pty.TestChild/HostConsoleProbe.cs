using System.Diagnostics;
using System.Text;
using Icod.Pty;
using Icod.Pty.Sample;

internal static class HostConsoleProbe {
	private static string DotNet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
	internal static async Task<int> ForwardChunksAsync() {
		byte[] expected = Encoding.UTF8.GetBytes("雪\u001b[A\u001b[12;34R");
		using MemoryStream destination = new();
		using HostConsole console = new FragmentedInputConsole(expected);
		await InteractiveSession.ForwardInputAsync(destination, console, CancellationToken.None);
		byte[] actual = destination.ToArray();
		Console.WriteLine("FORWARDED:" + Convert.ToHexString(actual));
		return expected.SequenceEqual(actual) ? 0 : 2;
	}
	private sealed class FragmentedInputConsole(byte[] bytes) : HostConsole {
		private int offset;
		internal override Stream Output => Stream.Null;
		internal override PtySize? GetSize() => null;
		internal override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token) {
			token.ThrowIfCancellationRequested();
			if (offset == bytes.Length) return ValueTask.FromResult(0);
			buffer.Span[0] = bytes[offset++]; return ValueTask.FromResult(1);
		}
		public override void Dispose() { }
	}
	internal static async Task<int> RunAsync(string scenario, string sample, string pidFile) {
		byte[] before = TerminalModes.Snapshot();
		int code;
		if (scenario is "partial-setup" or "cancel-before-read" or "output-failure" or "drain-timeout") code = await RunFaultAsync(scenario);
		else {
			ProcessStartInfo start = new(DotNet) { UseShellExecute = false };
			start.ArgumentList.Add(sample);
			if (scenario == "start-failure") start.ArgumentList.Add("missing-pty-executable-" + Guid.NewGuid().ToString("N"));
			else {
				start.ArgumentList.Add(DotNet); start.ArgumentList.Add(typeof(HostConsoleProbe).Assembly.Location);
				start.ArgumentList.Add(scenario == "retained-terminal" ? "hold-terminal-open" : "exit");
				if (scenario == "retained-terminal") start.ArgumentList.Add(pidFile);
			}
			using Process process = Process.Start(start)!;
			try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); code = process.ExitCode; }
			finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
		}
		Console.WriteLine("SAMPLE-EXIT:" + code);
		byte[] after = TerminalModes.Snapshot();
		Console.WriteLine("RESTORED:" + before.SequenceEqual(after));
		if (!before.SequenceEqual(after)) { Console.WriteLine("STATE-BEFORE:" + Convert.ToHexString(before)); Console.WriteLine("STATE-AFTER:" + Convert.ToHexString(after)); }
		Console.WriteLine("PROBE-READY");
		using StreamReader reader = new(Console.OpenStandardInput(), Encoding.UTF8);
		string? followup = reader.ReadLine();
		Console.WriteLine("FOLLOWUP-READ:" + System.Text.Json.JsonSerializer.Serialize(followup));
		if (followup != "followup") return 2;
		Console.WriteLine("FOLLOWUP-ACK"); return 0;
	}
	private static async Task<int> RunFaultAsync(string scenario) {
		try {
			if (scenario == "partial-setup") HostConsoleFaults.AfterModeChange = () => throw new IOException("Injected partial setup failure.");
			using HostConsole console = HostConsole.Open();
			if (scenario == "cancel-before-read") {
				using CancellationTokenSource cancel = new();
				HostConsoleFaults.BeforeRead = cancel.Cancel;
				try { await console.ReadAsync(new byte[32], cancel.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
				catch (OperationCanceledException) { return 1; }
				throw new InvalidOperationException("Input cancellation was not observed.");
			}
			PtyStartInfo start = new(DotNet);
			start.ArgumentList.Add(typeof(HostConsoleProbe).Assembly.Location); start.ArgumentList.Add(scenario == "drain-timeout" ? "exit" : "flood");
			using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
			await InteractiveSession.RunAsync(start, new FailingOutputConsole(console, scenario == "drain-timeout"), timeout.Token);
			throw new InvalidOperationException("Output failure was not observed.");
		} catch (Exception error) when (error is IOException or TimeoutException) { Console.Error.WriteLine(error.Message); return 1; }
		finally { HostConsoleFaults.AfterModeChange = null; HostConsoleFaults.BeforeRead = null; }
	}
	internal static async Task<int> HoldTerminalAsync(string pidFile) {
		ProcessStartInfo start = new(DotNet) { UseShellExecute = false };
		start.ArgumentList.Add(typeof(HostConsoleProbe).Assembly.Location); start.ArgumentList.Add("retained-holder"); start.ArgumentList.Add(pidFile);
		using Process child = Process.Start(start)!;
		// Publish the PID in the holder only after its SIGHUP handler is installed.
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
		while (!File.Exists(pidFile)) { if (child.HasExited) throw new IOException("Holder exited before readiness."); await Task.Delay(10, timeout.Token); }
		Console.WriteLine("HOLDER-READY"); return 0;
	}
	private sealed class FailingOutputConsole(HostConsole inner, bool block) : HostConsole {
		internal override Stream Output { get; } = block ? new BlockingStream() : new FailingStream();
		internal override PtySize? GetSize() => inner.GetSize();
		internal override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token) => inner.ReadAsync(buffer, token);
		public override void Dispose() { }
	}
	private sealed class FailingStream : MemoryStream {
		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException(new IOException("Injected output failure."));
	}
	private sealed class BlockingStream : MemoryStream {
		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => await Task.Delay(Timeout.Infinite, cancellationToken);
	}
}
