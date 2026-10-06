using System.Reflection;
using System.Text;

namespace Icod.Pty.Sample;

internal static class AutomationSmokeChecks {
	private static readonly byte[] BinaryMarker = [0xff, 0x00, 0xfe, 0x80];

	internal static async Task<int> RunAsync() {
		using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
		using MemoryStream output = new();
		await using PtySession session = await PtySession.StartAsync(Self("--automation-child"), new(output) {
			Automation = new() { MaxBufferedOutputBytes = 4096 }
		}, deadline.Token);

		PtyScriptResult first = await PtyScriptRunner.RunAsync(session, [
			PtyScriptStep.Expect("ICOD-AUTOMATION-READY>"u8.ToArray(), TimeSpan.FromSeconds(5)),
			PtyScriptStep.Send(Line("go")),
			PtyScriptStep.Expect(BinaryMarker, TimeSpan.FromSeconds(5)),
			PtyScriptStep.Expect("ONE"u8.ToArray(), TimeSpan.FromSeconds(5)),
			PtyScriptStep.Expect("ABSENT"u8.ToArray(), TimeSpan.FromMilliseconds(25))
		], deadline.Token);
		Require(first == new PtyScriptResult(PtyScriptStatus.ExpectationFailed, 4, 4, PtyExpectStatus.TimedOut),
			"Automation timeout did not identify the expected script step.");

		PtyScriptResult retry = await PtyScriptRunner.RunAsync(session, [
			PtyScriptStep.Expect("TWO"u8.ToArray(), TimeSpan.FromSeconds(5)),
			PtyScriptStep.Expect("RETRY"u8.ToArray(), TimeSpan.FromSeconds(5)),
			PtyScriptStep.Send(Line("quit"))
		], deadline.Token);
		Require(retry == new PtyScriptResult(PtyScriptStatus.Completed, 3, null, null), "Automation retry did not complete.");

		PtySessionResult result = await session.Completion.WaitAsync(deadline.Token);
		Require(result.ExitCode == 29 && result.OutputStatus == PtySessionOutputStatus.EndOfStream,
			"Automation child did not complete normally.");
		Console.WriteLine("PTY automation smoke check passed.");
		return 0;
	}

	internal static async Task<int> RunChildAsync() {
		Stream output = Console.OpenStandardOutput();
		await output.WriteAsync("ICOD-AUTOMATION-READY>"u8.ToArray());
		await output.FlushAsync();
		Require(await Console.In.ReadLineAsync() == "go", "Automation child expected go.");

		await output.WriteAsync(BinaryMarker.AsMemory(0, 2));
		await output.FlushAsync();
		await Task.Delay(25);
		await output.WriteAsync(BinaryMarker.AsMemory(2));
		await output.WriteAsync("ONE:TWO:RETRY"u8.ToArray());
		await output.FlushAsync();
		Require(await Console.In.ReadLineAsync() == "quit", "Automation child expected quit.");
		return 29;
	}

	private static byte[] Line(string value) => Encoding.UTF8.GetBytes(value + (OperatingSystem.IsWindows() ? "\r\n" : "\n"));
	private static PtyStartInfo Self(params string[] arguments) {
		string executable = Environment.ProcessPath ?? throw new IOException("Cannot identify the sample executable.");
		PtyStartInfo start = new(executable);
		if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
			start.ArgumentList.Add(Assembly.GetEntryAssembly()?.Location ?? throw new IOException("Cannot identify the sample assembly."));
		foreach (string argument in arguments) start.ArgumentList.Add(argument);
		return start;
	}
	private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
