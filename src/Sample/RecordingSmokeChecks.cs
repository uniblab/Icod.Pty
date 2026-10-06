using System.Reflection;

namespace Icod.Pty.Sample;

internal static class RecordingSmokeChecks {
	internal static async Task<int> RunAsync() {
		using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
		using MemoryStream output = new(), recording = new();
		await using (PtySession session = await PtySession.StartAsync(Self("--recording-child"), new(output) { Recording = new(recording) }, deadline.Token)) {
			session.Resize(new PtySize(100, 40)); await session.WriteAsync(Line(), deadline.Token);
			PtySessionResult sessionResult = await session.Completion.WaitAsync(deadline.Token);
			PtyRecordingResult recordingResult = await session.RecordingCompletion.WaitAsync(deadline.Token);
			Require(sessionResult.ExitCode == 19 && sessionResult.OutputStatus == PtySessionOutputStatus.EndOfStream, "Recorded session did not complete.");
			Require(recordingResult.Status == PtyRecordingStatus.Complete, "Recording did not complete.");
		}
		recording.Position = 0; using MemoryStream replay = new(); await using (PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(recording, cancellationToken: deadline.Token)) {
			bool resized = false; PtyRecordingEvent? item;
			while ((item = await reader.ReadAsync(deadline.Token)) != null) {
				if (item.Kind == PtyRecordingEventKind.Output) await replay.WriteAsync(item.Output, deadline.Token);
				else if (item.Size == new PtySize(100, 40)) resized = true;
			}
			Require(reader.FinalStatus == PtyRecordingStatus.Complete && resized, "Recording event sequence was incomplete.");
		}
		Require(output.ToArray().SequenceEqual(replay.ToArray()), "Replayed bytes differ from accepted output.");

		using MemoryStream limitedOutput = new(), limitedRecording = new();
		await using (PtySession session = await PtySession.StartAsync(Self("--recording-child"), new(limitedOutput) {
			Recording = new(limitedRecording) { MaxBytes = PtyRecordingOptions.MinimumBytes }
		}, deadline.Token)) {
			await session.WriteAsync(Line(), deadline.Token); await session.Completion.WaitAsync(deadline.Token);
			Require((await session.RecordingCompletion).Status == PtyRecordingStatus.Truncated, "Bounded recording did not report truncation.");
		}
		byte[] prefix = limitedRecording.ToArray(); limitedRecording.Position = 0;
		await using (PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(limitedRecording, cancellationToken: deadline.Token)) {
			Require(await reader.ReadAsync(deadline.Token) == null && reader.FinalStatus == PtyRecordingStatus.Truncated, "Truncated recording was not valid.");
		}
		try {
			await using PtyRecordingReader damaged = await PtyRecordingReader.OpenAsync(new MemoryStream(prefix[..^1]), cancellationToken: deadline.Token);
			while (await damaged.ReadAsync(deadline.Token) != null) { }
		} catch (PtyRecordingFormatException) { Console.WriteLine("PTY recording smoke check passed."); return 0; }
		throw new IOException("Damaged recording was accepted.");
	}
	internal static async Task<int> RunChildAsync() {
		_ = await Console.In.ReadLineAsync();
		await Console.OpenStandardOutput().WriteAsync(new byte[] { 0, 255, 65, 10 }); return 19;
	}
	private static byte[] Line() => System.Text.Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? "go\r\n" : "go\n");
	private static PtyStartInfo Self(params string[] arguments) {
		string executable = Environment.ProcessPath ?? throw new IOException("Cannot identify the sample executable."); PtyStartInfo start = new(executable);
		if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
			start.ArgumentList.Add(Assembly.GetEntryAssembly()?.Location ?? throw new IOException("Cannot identify the sample assembly."));
		foreach (string argument in arguments) start.ArgumentList.Add(argument); return start;
	}
	private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
