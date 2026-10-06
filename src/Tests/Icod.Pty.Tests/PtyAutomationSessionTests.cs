using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyAutomationSessionTests {
	[Fact]
	public async Task Startup_output_is_retained_after_a_successful_destination_write() {
		using MemoryStream destination = new(); ControlledBackend backend = new() { Output = new MemoryStream("prompt> "u8.ToArray()) };
		PtySession session = await SessionTestSupport.Start(backend, destination, automation: new());
		try {
			Assert.Equal(PtySessionOutputStatus.EndOfStream, await session.OutputCompletion.WaitAsync(TimeSpan.FromSeconds(5)));
			Assert.Equal(new(PtyExpectStatus.Matched, 8),
				await session.ExpectAsync("prompt> "u8.ToArray(), TimeSpan.FromSeconds(1)));
			Assert.Equal("prompt> "u8.ToArray(), destination.ToArray());
		} finally { backend.Completion.TrySetResult(0); await session.DisposeAsync(); }
	}

	[Fact]
	public async Task A_partially_failed_destination_write_is_not_observed() {
		IOException failure = new("destination"); using GateWriteStream destination = new() { Prefix = 3, Failure = failure };
		ControlledBackend backend = new() { Output = new MemoryStream("partial"u8.ToArray()) };
		PtySession session = await SessionTestSupport.Start(backend, destination, automation: new());
		try {
			await destination.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			Task<PtyExpectResult> pending = session.ExpectAsync("par"u8.ToArray(), TimeSpan.FromSeconds(1));
			destination.Release.SetResult();

			Assert.Equal(new(PtyExpectStatus.OutputFaulted, 0), await pending);
			Assert.Equal("par"u8.ToArray(), destination.ToArray());
			Assert.Equal(PtySessionOutputStatus.Faulted, await session.OutputCompletion);
		} finally {
			destination.Release.TrySetResult();
			try { await session.DisposeAsync(); } catch (AggregateException) { }
		}
	}

	[Fact]
	public async Task Recorder_failure_does_not_hide_accepted_output() {
		using MemoryStream destination = new(); using AsyncFailRecordingStream recording = new(new IOException("recording"));
		ControlledBackend backend = new() { Output = new MemoryStream("OK"u8.ToArray()) };
		PtySession session = await SessionTestSupport.Start(backend, destination, recording: new(recording), automation: new());
		try {
			Assert.Equal(PtyExpectStatus.Matched, (await session.ExpectAsync("OK"u8.ToArray(), TimeSpan.FromSeconds(1))).Status);
			backend.Completion.SetResult(0);
			Assert.Equal(PtyRecordingStatus.Faulted, (await session.RecordingCompletion).Status);
		} finally { backend.Completion.TrySetResult(0); await session.DisposeAsync(); }
	}

	[Fact]
	public async Task Automation_overrun_does_not_truncate_live_output() {
		byte[] bytes = Enumerable.Range(0, 4096).Select(index => (byte)index).ToArray(); using MemoryStream destination = new(), recording = new();
		ControlledBackend backend = new() { Output = new MemoryStream(bytes) };
		PtySession session = await SessionTestSupport.Start(backend, destination, recording: new(recording),
			automation: new() { MaxBufferedOutputBytes = 16 });
		try {
			Assert.Equal(PtySessionOutputStatus.EndOfStream, await session.OutputCompletion.WaitAsync(TimeSpan.FromSeconds(5)));
			Assert.Equal(PtyExpectStatus.BufferLimitExceeded,
				(await session.ExpectAsync(new byte[] { 1 }, TimeSpan.FromSeconds(1))).Status);
			Assert.Equal(bytes, destination.ToArray());
			backend.Completion.SetResult(0);
			Assert.Equal(PtyRecordingStatus.Complete, (await session.RecordingCompletion).Status);
		} finally { backend.Completion.TrySetResult(0); await session.DisposeAsync(); }
	}

	[Fact]
	public async Task Primary_exit_does_not_end_matching_while_descendant_output_is_open() {
		using FeedStream source = new(); using MemoryStream destination = new(); ControlledBackend backend = new() { Output = source };
		PtySession session = await SessionTestSupport.Start(backend, destination, drain: TimeSpan.FromSeconds(2), automation: new());
		try {
			Task<PtyExpectResult> pending = session.ExpectAsync("late"u8.ToArray(), TimeSpan.FromSeconds(1));
			backend.Completion.SetResult(0);
			await SessionTestSupport.Until(() => session.GetDiagnostics().Phase == PtySessionPhase.Draining);
			source.Feed("late"u8.ToArray());

			Assert.Equal(PtyExpectStatus.Matched, (await pending).Status);
			source.End();
			Assert.Equal(PtySessionOutputStatus.EndOfStream, (await session.Completion.WaitAsync(TimeSpan.FromSeconds(5))).OutputStatus);
		} finally { source.End(); await session.DisposeAsync(); }
	}

	[Fact]
	public async Task Drain_timeout_settles_a_pending_expectation() {
		using FeedStream source = new(); using MemoryStream destination = new(); ControlledBackend backend = new() { Output = source };
		PtySession session = await SessionTestSupport.Start(backend, destination, drain: TimeSpan.FromMilliseconds(25), automation: new());
		Task<PtyExpectResult> pending = session.ExpectAsync("missing"u8.ToArray(), TimeSpan.FromSeconds(2));

		backend.Completion.SetResult(0);

		Assert.Equal(new(PtyExpectStatus.OutputTimedOut, 0), await pending.WaitAsync(TimeSpan.FromSeconds(5)));
		Assert.Equal(PtySessionOutputStatus.TimedOut, (await session.Completion).OutputStatus);
		await session.DisposeAsync();
	}

	[Fact]
	public async Task Disposal_settles_a_pending_expectation_as_stopped() {
		using FeedStream source = new(); using MemoryStream destination = new(); ControlledBackend backend = new() { Output = source };
		PtySession session = await SessionTestSupport.Start(backend, destination, automation: new());
		Task<PtyExpectResult> pending = session.ExpectAsync("missing"u8.ToArray(), TimeSpan.FromSeconds(2));

		Task dispose = session.DisposeAsync().AsTask();

		Assert.Equal(new(PtyExpectStatus.OutputStopped, 0), await pending.WaitAsync(TimeSpan.FromSeconds(5)));
		await dispose.WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Fact]
	public async Task Disabled_automation_keeps_the_existing_output_path() {
		using MemoryStream destination = new(); ControlledBackend backend = new() { Output = new MemoryStream("OK"u8.ToArray()) };
		PtySession session = await SessionTestSupport.Start(backend, destination);
		try {
			Assert.Equal(PtySessionOutputStatus.EndOfStream, await session.OutputCompletion.WaitAsync(TimeSpan.FromSeconds(5)));
			Assert.Equal("OK"u8.ToArray(), destination.ToArray());
			await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExpectAsync("OK"u8.ToArray(), TimeSpan.FromSeconds(1)));
		} finally { backend.Completion.TrySetResult(0); await session.DisposeAsync(); }
	}
}
