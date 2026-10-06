using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyRecordingIntegrationTests {
	[Fact]
	public async Task Session_records_accepted_output_and_successful_resize() {
		byte[] bytes = [0, 255, 65, 66]; using MemoryStream output = new(), recording = new();
		ControlledBackend backend = new() { Output = new MemoryStream(bytes) };
		PtySession session = await SessionTestSupport.Start(backend, output, recording: new(recording));
		session.Resize(new PtySize(100, 40)); backend.Completion.SetResult(0);
		PtySessionResult sessionResult = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
		PtyRecordingResult recordingResult = await session.RecordingCompletion;
		Assert.Equal(PtySessionOutputStatus.EndOfStream, sessionResult.OutputStatus);
		Assert.Equal(PtyRecordingStatus.Complete, recordingResult.Status); Assert.Equal(bytes, output.ToArray());
		recording.Position = 0; await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(recording);
		List<PtyRecordingEvent> events = []; PtyRecordingEvent? item; while ((item = await reader.ReadAsync()) != null) events.Add(item);
		Assert.Contains(events, e => e.Kind == PtyRecordingEventKind.Output && e.Output.Span.SequenceEqual(bytes));
		Assert.Contains(events, e => e.Kind == PtyRecordingEventKind.Resize && e.Size == new PtySize(100, 40));
		await session.DisposeAsync();
	}

	[Fact]
	public async Task Recorder_failure_does_not_replace_live_output_result() {
		using MemoryStream output = new(); IOException failure = new("recording sink"); using AsyncFailRecordingStream recording = new(failure);
		ControlledBackend backend = new() { Output = new MemoryStream("OK"u8.ToArray()) };
		PtySession session = await SessionTestSupport.Start(backend, output, recording: new(recording)); backend.Completion.SetResult(0);
		PtySessionResult sessionResult = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
		PtyRecordingResult recordingResult = await session.RecordingCompletion;
		Assert.Equal(PtySessionOutputStatus.EndOfStream, sessionResult.OutputStatus); Assert.Empty(sessionResult.Failures); Assert.Equal("OK"u8.ToArray(), output.ToArray());
		Assert.Equal(PtyRecordingStatus.Faulted, recordingResult.Status); Assert.Same(failure, recordingResult.Exception);
		await session.DisposeAsync();
	}

	[Fact]
	public async Task Failed_destination_chunk_and_failed_resize_are_not_recorded() {
		using GateWriteStream output = new() { Failure = new IOException("output") }; using MemoryStream recording = new();
		ControlledBackend backend = new() { Output = new MemoryStream("NOT-RECORDED"u8.ToArray()), ResizeFailure = new IOException("resize") };
		PtySession session = await SessionTestSupport.Start(backend, output, recording: new(recording));
		await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.ThrowsAsync<IOException>(() => Task.Run(() => session.Resize(new PtySize(90, 30))));
		output.Release.SetResult();
		PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(PtySessionOutputStatus.Faulted, result.OutputStatus);
		Assert.Equal(PtyRecordingStatus.Stopped, (await session.RecordingCompletion).Status);
		recording.Position = 0; await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(recording);
		Assert.Null(await reader.ReadAsync());
		await Assert.ThrowsAsync<AggregateException>(async () => await session.DisposeAsync());
	}

	[Fact]
	public async Task Disabled_recording_completion_is_already_available() {
		using MemoryStream output = new(); await using PtySession session = await SessionTestSupport.Start(new(), output);
		Assert.Same(PtyRecordingResult.Disabled, await session.RecordingCompletion);
	}

	[Fact]
	public async Task Cap_does_not_truncate_live_output() {
		byte[] bytes = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray(); using MemoryStream output = new(), recording = new();
		ControlledBackend backend = new() { Output = new MemoryStream(bytes) };
		PtySession session = await SessionTestSupport.Start(backend, output, recording: new(recording) { MaxBytes = 48 }); backend.Completion.SetResult(0);
		await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(bytes, output.ToArray()); Assert.Equal(PtyRecordingStatus.Truncated, (await session.RecordingCompletion).Status); Assert.Equal(32, recording.Length);
		await session.DisposeAsync();
	}

	[Fact]
	public async Task Drain_timeout_stops_and_closes_owned_recording() {
		using MemoryStream output = new(); TrackingStream recording = new(); FeedStream source = new(); ControlledBackend backend = new() { Output = source };
		PtySession session = await SessionTestSupport.Start(backend, output, drain: TimeSpan.FromMilliseconds(20), leaveOpen: true,
			recording: new(recording) { LeaveOpen = false });
		source.Feed("PREFIX"u8.ToArray()); await SessionTestSupport.Until(() => output.Length == 6); backend.Completion.SetResult(0);
		PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(PtySessionOutputStatus.TimedOut, result.OutputStatus); Assert.Equal(PtyRecordingStatus.Stopped, (await session.RecordingCompletion).Status);
		Assert.Equal(1, recording.Flushes); Assert.Equal(1, recording.Disposals);
		await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => session.DisposeAsync().AsTask())); Assert.Equal(1, recording.Disposals);
	}

	[Fact]
	public async Task Graceful_shutdown_completes_recording_after_output_eof() {
		using MemoryStream output = new(), recording = new(); ControlledBackend backend = new() { Output = new MemoryStream("BYE"u8.ToArray()) };
		PtySession session = await SessionTestSupport.Start(backend, output, recording: new(recording));
		Task<PtyShutdownResult> shutdown = session.ShutdownAsync(new() { Request = "quit"u8.ToArray(), GracePeriod = TimeSpan.FromSeconds(5) });
		await SessionTestSupport.Until(() => backend.Input.Length == 4); backend.Completion.SetResult(23);
		Assert.Equal(PtyShutdownStatus.Exited, (await shutdown).Status);
		Assert.Equal(PtySessionOutputStatus.EndOfStream, (await session.Completion).OutputStatus);
		Assert.Equal(PtyRecordingStatus.Complete, (await session.RecordingCompletion).Status);
		await session.DisposeAsync();
	}

	[Fact]
	public async Task Slow_recording_sink_settles_when_drain_times_out() {
		using MemoryStream output = new(); using GateWriteStream recording = new(); FeedStream source = new();
		ControlledBackend backend = new() { Output = source };
		PtySession session = await SessionTestSupport.Start(backend, output, drain: TimeSpan.FromMilliseconds(20),
			recording: new(recording));
		try {
			source.Feed("PREFIX"u8.ToArray()); await recording.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); backend.Completion.SetResult(0);
			PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.Equal(PtySessionOutputStatus.TimedOut, result.OutputStatus);
			Assert.Equal(PtyRecordingStatus.Faulted, (await session.RecordingCompletion).Status);
		} finally { recording.Release.TrySetResult(); source.End(); await session.DisposeAsync(); }
	}

	[Fact]
	public async Task Repeated_output_and_resize_storms_remain_bounded() {
		for (int iteration = 0; iteration < 20; iteration++) {
			byte[] bytes = new byte[64 * 1024]; new Random(iteration).NextBytes(bytes);
			using MemoryStream output = new(), recording = new(); ControlledBackend backend = new() { Output = new MemoryStream(bytes) };
			PtySession session = await SessionTestSupport.Start(backend, output, recording: new(recording) { MaxBytes = 256 });
			await Task.WhenAll(Enumerable.Range(0, 50).Select(index => Task.Run(() => session.Resize(new PtySize(80 + index, 24 + index)))));
			backend.Completion.SetResult(0); await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.Equal(bytes, output.ToArray()); Assert.True(recording.Length <= 256); Assert.Equal(PtyRecordingStatus.Truncated, (await session.RecordingCompletion).Status);
			await session.DisposeAsync();
		}
	}
}
