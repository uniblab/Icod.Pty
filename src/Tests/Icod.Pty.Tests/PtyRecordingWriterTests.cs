using Icod.Pty.Recording;
using Icod.Pty.Session;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyRecordingWriterTests {
	[Fact]
	public async Task Writer_splits_output_and_round_trips_resize_in_order() {
		using MemoryStream stream = new(); long ticks = 0;
		SessionRecorder writer = new(new(stream, true, 1_000_000), new PtySize(80, 24), () => ticks += 5);
		byte[] output = Enumerable.Range(0, 20_000).Select(i => (byte)i).ToArray();
		await writer.RecordOutputAsync(output, default);
		writer.RecordResize(new PtySize(100, 40));
		PtyRecordingResult result = await writer.FinishAsync(PtyRecordingStatus.Complete);
		Assert.Equal(PtyRecordingStatus.Complete, result.Status); Assert.Equal(3, result.EventCount); Assert.Equal(stream.Length, result.BytesWritten);

		stream.Position = 0; await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(stream);
		PtyRecordingEvent first = (await reader.ReadAsync())!, second = (await reader.ReadAsync())!, resize = (await reader.ReadAsync())!;
		Assert.Equal(16 * 1024, first.Output.Length); Assert.Equal(20_000 - 16 * 1024, second.Output.Length);
		Assert.Equal(output, first.Output.ToArray().Concat(second.Output.ToArray()).ToArray());
		Assert.Equal(new PtySize(100, 40), resize.Size); Assert.True(first.Elapsed <= second.Elapsed && second.Elapsed <= resize.Elapsed);
		Assert.Null(await reader.ReadAsync()); Assert.Equal(PtyRecordingStatus.Complete, reader.FinalStatus);
	}

	[Fact]
	public async Task Cap_writes_one_truncation_marker_and_ignores_later_events() {
		using MemoryStream stream = new(); SessionRecorder writer = new(new(stream, true, 48), new PtySize(80, 24), () => 1);
		await writer.RecordOutputAsync(new byte[17], default); writer.RecordResize(new PtySize(81, 25));
		PtyRecordingResult result = await writer.FinishAsync(PtyRecordingStatus.Complete);
		Assert.Equal(PtyRecordingStatus.Truncated, result.Status); Assert.Equal(32, result.BytesWritten); Assert.Equal(0, result.EventCount); Assert.Equal(32, stream.Length);
		stream.Position = 0; await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(stream);
		Assert.Null(await reader.ReadAsync()); Assert.Equal(PtyRecordingStatus.Truncated, reader.FinalStatus);
	}

	[Fact]
	public async Task Sink_failure_is_reported_without_terminal_content() {
		IOException failure = new("sink failed"); using FailingRecordingStream stream = new(failure, 2);
		SessionRecorder writer = new(new(stream, true, 4096), new PtySize(80, 24), () => 1);
		await writer.RecordOutputAsync("SECRET"u8.ToArray(), default);
		PtyRecordingResult result = await writer.FinishAsync(PtyRecordingStatus.Complete);
		Assert.Equal(PtyRecordingStatus.Faulted, result.Status); Assert.Same(failure, result.Exception);
		Assert.DoesNotContain("SECRET", result.Exception!.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Finish_flushes_and_disposes_owned_stream_once() {
		TrackingStream stream = new(); SessionRecorder writer = new(new(stream, false, 4096), new PtySize(80, 24), () => 1);
		PtyRecordingResult first = await writer.FinishAsync(PtyRecordingStatus.Stopped), second = await writer.FinishAsync(PtyRecordingStatus.Complete);
		Assert.Same(first, second); Assert.Equal(PtyRecordingStatus.Stopped, first.Status); Assert.Equal(1, stream.Flushes); Assert.Equal(1, stream.Disposals);
	}

	[Fact]
	public async Task Cancellation_during_sink_write_is_an_independent_recording_fault() {
		using CancellationTokenSource stop = new(); using CancelRecordingStream stream = new(stop);
		SessionRecorder writer = new(new(stream, true, 4096), new PtySize(80, 24), () => 1);
		await writer.RecordOutputAsync("accepted"u8.ToArray(), stop.Token);
		PtyRecordingResult result = await writer.FinishAsync(PtyRecordingStatus.Stopped);
		Assert.Equal(PtyRecordingStatus.Faulted, result.Status); Assert.IsAssignableFrom<OperationCanceledException>(result.Exception);
	}
}

internal sealed class FailingRecordingStream(Exception failure, int failOnCall) : MemoryStream {
	private int calls;
	public override void Write(byte[] buffer, int offset, int count) { if (++calls == failOnCall) throw failure; base.Write(buffer, offset, count); }
	public override void Write(ReadOnlySpan<byte> buffer) { if (++calls == failOnCall) throw failure; base.Write(buffer); }
	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) {
		if (++calls == failOnCall) return ValueTask.FromException(failure); base.Write(buffer.Span); return ValueTask.CompletedTask;
	}
}

internal sealed class CancelRecordingStream(CancellationTokenSource stop) : MemoryStream {
	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) {
		stop.Cancel(); return ValueTask.FromCanceled(token);
	}
}
