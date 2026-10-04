using System.Reflection;
using System.Text;

namespace Icod.Pty.Sample;

internal static class PackageSmokeChecks {
	private static string Shell => OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : "/bin/sh";
	private static byte[] Line(string value) => Encoding.UTF8.GetBytes(value + (OperatingSystem.IsWindows() ? "\r" : "\n"));
	internal static async Task<int> RunLifecycleAsync() {
		using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
		await using PtyProcess process = await PtyProcess.StartAsync(new(Shell), deadline.Token);
		using StreamReader reader = new(process.Output, Encoding.UTF8, false, 4096, true);
		Task<string> output = reader.ReadToEndAsync(deadline.Token);
		try {
			PtyShutdownResult result = await process.ShutdownAsync(new() { Request = Line("exit"), ForceTermination = false }, deadline.Token);
			Require(result.Status == PtyShutdownStatus.Exited && result.ExitCode == 0 && !result.ForcedTerminationRequested, "Graceful shutdown failed.");
			await output;
		} finally { deadline.Cancel(); try { await output; } catch (Exception) { /* Observe cancellation before disposal. */ } }
		Console.WriteLine("PTY lifecycle smoke check passed."); return 0;
	}
	internal static async Task<int> RunCancelledStartAsync() {
		using CancellationTokenSource cancelled = new(); cancelled.Cancel();
		try { await using PtyProcess unexpected = await PtyProcess.StartAsync(new(Shell), cancelled.Token); }
		catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { Console.WriteLine("PTY cancelled-start smoke check passed."); return 0; }
		throw new IOException("A pre-cancelled startup returned a process.");
	}
	internal static async Task<int> RunInterruptAsync() {
		using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
		string executable = Environment.ProcessPath ?? throw new IOException("Cannot identify the verification executable.");
		PtyStartInfo start = new(executable) { Size = new PtySize(100, 30) };
		if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
			start.ArgumentList.Add(Assembly.GetEntryAssembly()?.Location ?? throw new IOException("Cannot identify the verification assembly."));
		start.ArgumentList.Add("--interrupt-child");
		await using PtyProcess process = await PtyProcess.StartAsync(start, deadline.Token);
		TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously), interrupted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<string> output = CaptureAsync(process.Output, ready, interrupted, deadline.Token);
		try {
			await ready.Task.WaitAsync(deadline.Token);
			await process.SendInterruptAsync(deadline.Token);
			await interrupted.Task.WaitAsync(deadline.Token);
			Require(!process.HasExited, "The verification child exited on Ctrl+C.");
			PtyShutdownResult result = await process.ShutdownAsync(new() { Request = Line("quit") }, deadline.Token);
			Require(result.Status == PtyShutdownStatus.Exited && result.ExitCode == 23 && !result.ForcedTerminationRequested, "Interrupted child did not exit on request.");
			Require((await output).Contains("ICOD-INTERRUPT-BYE", StringComparison.Ordinal), "Final interrupt-check output was missing.");
		} finally {
			deadline.Cancel();
			try { await output; } catch (Exception) { /* Observe the sole output reader before disposal. */ }
		}
		Console.WriteLine("PTY interrupt smoke check passed."); return 0;
	}
	private static async Task<string> CaptureAsync(Stream stream, TaskCompletionSource ready, TaskCompletionSource interrupted, CancellationToken token) {
		using StreamReader reader = new(stream, Encoding.UTF8, false, 4096, true);
		StringBuilder text = new(); char[] buffer = new char[1024];
		try {
			int count;
			while ((count = await reader.ReadAsync(buffer, token)) != 0) {
				text.Append(buffer, 0, count); string value = text.ToString();
				if (value.Contains("ICOD-INTERRUPT-READY", StringComparison.Ordinal)) ready.TrySetResult();
				if (value.Contains("ICOD-INTERRUPT-ACK", StringComparison.Ordinal)) interrupted.TrySetResult();
				Require(text.Length <= 100000, "Interrupt-check output exceeded its limit.");
			}
			return text.ToString();
		} finally {
			// Complete pending handshakes on EOF/failure, without unobserved exception tasks.
			ready.TrySetCanceled(); interrupted.TrySetCanceled();
		}
	}
	internal static async Task<int> RunInterruptChildAsync() {
		using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
		Console.InputEncoding = Encoding.UTF8; Console.OutputEncoding = Encoding.UTF8;
		Console.TreatControlCAsInput = false;
		TaskCompletionSource interrupt = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; interrupt.TrySetResult(); };
		Console.CancelKeyPress += handler;
		try {
			Console.WriteLine("ICOD-INTERRUPT-READY");
			// Start reading commands only after Ctrl+C; a Windows console read in flight
			// can complete with zero bytes on interruption.
			await interrupt.Task.WaitAsync(deadline.Token);
			Console.WriteLine("ICOD-INTERRUPT-ACK");
			using StreamReader reader = new(Console.OpenStandardInput(), Encoding.UTF8);
			string? command = await Task.Run(reader.ReadLine).WaitAsync(deadline.Token);
			Require(command == "quit", "Verification child expected quit.");
			Console.WriteLine("ICOD-INTERRUPT-BYE"); return 23;
		} finally { Console.CancelKeyPress -= handler; }
	}
	private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
