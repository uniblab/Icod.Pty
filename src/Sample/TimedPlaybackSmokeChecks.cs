using System.Buffers.Binary;
using System.Diagnostics;

namespace Icod.Pty.Sample;

internal static class TimedPlaybackSmokeChecks {
	internal static async Task<int> RunAsync() {
		using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
		using MemoryStream recording = CreateRecording();
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(recording, cancellationToken: deadline.Token);
		List<PtyRecordingEvent> events = []; Stopwatch elapsed = Stopwatch.StartNew(); TimeSpan? firstDispatch = null;
		PtyRecordingReplayResult result = await reader.PlayTimedAsync((item, _) => {
			firstDispatch ??= elapsed.Elapsed; events.Add(item); return ValueTask.CompletedTask;
		}, cancellationToken: deadline.Token);

		Require(firstDispatch >= TimeSpan.FromMilliseconds(10), "The first event was not paced from the playback origin.");
		Require(events.Count == 2, "Timed playback event count was incorrect.");
		Require(events[0].Kind == PtyRecordingEventKind.Output && events[0].Output.Span.SequenceEqual(new byte[] { 0, 255, 65 }),
			"Timed playback changed binary output.");
		Require(events[1].Kind == PtyRecordingEventKind.Resize && events[1].Size == new PtySize(100, 40),
			"Timed playback changed resize order or value.");
		Require(result == new PtyRecordingReplayResult(PtyRecordingStatus.Complete, 3, 2), "Timed playback result was incorrect.");
		Console.WriteLine("PTY timed playback smoke check passed."); return 0;
	}

	private static MemoryStream CreateRecording() {
		MemoryStream stream = new();
		stream.Write([0x49, 0x63, 0x6f, 0x64, 0x50, 0x74, 0x79, 0x00, 0x01, 0x00, 0x10, 0x00, 0x50, 0x00, 0x18, 0x00]);
		WriteFrame(stream, 1, TimeSpan.FromMilliseconds(40), [0, 255, 65]);
		byte[] size = new byte[4]; BinaryPrimitives.WriteUInt16LittleEndian(size, 100); BinaryPrimitives.WriteUInt16LittleEndian(size.AsSpan(2), 40);
		WriteFrame(stream, 2, TimeSpan.FromMilliseconds(60), size); WriteFrame(stream, 3, TimeSpan.FromMilliseconds(60), []);
		stream.Position = 0; return stream;
	}

	private static void WriteFrame(Stream stream, byte kind, TimeSpan elapsed, byte[] payload) {
		Span<byte> header = stackalloc byte[16]; header[0] = kind; BinaryPrimitives.WriteInt64LittleEndian(header[4..], elapsed.Ticks);
		BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)payload.Length); stream.Write(header); stream.Write(payload);
	}
	private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
