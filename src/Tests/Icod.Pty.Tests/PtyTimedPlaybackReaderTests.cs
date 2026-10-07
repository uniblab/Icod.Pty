using System.Buffers.Binary;
using System.Diagnostics;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyTimedPlaybackReaderTests {
	[Fact]
	public async Task Complete_record_dispatches_binary_output_and_resize_in_order() {
		byte[] bytes = Recording(PtyRecordingStatus.Complete,
			(Output(TimeSpan.FromMilliseconds(40), [0, 255, 65])),
			(Resize(TimeSpan.FromMilliseconds(40), new PtySize(100, 40))));
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(bytes));
		List<PtyRecordingEvent> events = []; Stopwatch elapsed = Stopwatch.StartNew();
		PtyRecordingReplayResult result = await reader.PlayTimedAsync((item, _) => { events.Add(item); return ValueTask.CompletedTask; });

		Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(20));
		Assert.Equal(new PtySize(80, 24), reader.InitialSize);
		Assert.Collection(events,
			item => { Assert.Equal(PtyRecordingEventKind.Output, item.Kind); Assert.Equal(new byte[] { 0, 255, 65 }, item.Output.ToArray()); },
			item => { Assert.Equal(PtyRecordingEventKind.Resize, item.Kind); Assert.Equal(new PtySize(100, 40), item.Size); });
		Assert.Equal(new(PtyRecordingStatus.Complete, 3, 2), result);
	}

	[Theory]
	[InlineData(PtyRecordingStatus.Truncated)]
	[InlineData(PtyRecordingStatus.Stopped)]
	public async Task Valid_prefix_returns_recorded_terminal_status(PtyRecordingStatus status) {
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(Recording(status, Output(TimeSpan.Zero, [7]))));
		int calls = 0; PtyRecordingReplayResult result = await reader.PlayTimedAsync((_, _) => { calls++; return ValueTask.CompletedTask; });
		Assert.Equal(status, result.Status); Assert.Equal(1, result.OutputBytes); Assert.Equal(1, result.EventCount); Assert.Equal(1, calls);
	}

	[Fact]
	public async Task Finished_reader_returns_status_with_zero_new_events() {
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(Recording(PtyRecordingStatus.Complete, Output(TimeSpan.Zero, [1]))));
		await reader.PlayTimedAsync((_, _) => ValueTask.CompletedTask);
		PtyRecordingReplayResult result = await reader.PlayTimedAsync((_, _) => throw new InvalidOperationException("must not run"));
		Assert.Equal(new(PtyRecordingStatus.Complete, 0, 0), result);
	}

	[Fact]
	public async Task Immediate_replay_does_not_honor_recorded_timing() {
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(Recording(PtyRecordingStatus.Complete,
			Output(TimeSpan.FromSeconds(10), [1, 2]), Resize(TimeSpan.FromSeconds(10), new PtySize(81, 25)))));
		using MemoryStream destination = new(); Stopwatch elapsed = Stopwatch.StartNew();
		PtyRecordingReplayResult result = await reader.ReplayAsync(destination);
		Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1)); Assert.Equal(new byte[] { 1, 2 }, destination.ToArray()); Assert.Equal(2, result.EventCount);
	}

	[Fact]
	public async Task Playback_captures_cap_before_yield() {
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(Recording(PtyRecordingStatus.Complete,
			Output(TimeSpan.Zero, [1]), Output(TimeSpan.FromMilliseconds(5), [2]))));
		PtyRecordingTimedPlaybackOptions options = new() { MaxEventElapsed = TimeSpan.FromMilliseconds(10) }; int calls = 0;
		PtyRecordingReplayResult result = await reader.PlayTimedAsync((_, _) => { calls++; options.MaxEventElapsed = TimeSpan.FromTicks(1); return ValueTask.CompletedTask; }, options);
		Assert.Equal(2, calls); Assert.Equal(2, result.EventCount);
	}

	[Fact]
	public async Task Pending_callback_rejects_concurrent_operations() {
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(Recording(PtyRecordingStatus.Complete, Output(TimeSpan.Zero, [1]))));
		TaskCompletionSource entered = Signal(), release = Signal();
		Task<PtyRecordingReplayResult> pending = reader.PlayTimedAsync(async (_, _) => { entered.SetResult(); await release.Task; });
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
		await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadAsync());
		await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReplayAsync(new MemoryStream()));
		await Assert.ThrowsAsync<InvalidOperationException>(() => reader.PlayTimedAsync((_, _) => ValueTask.CompletedTask));
		release.SetResult(); await pending;
	}

	[Fact]
	public async Task Callback_reentrant_read_fails_promptly() {
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(Recording(PtyRecordingStatus.Complete, Output(TimeSpan.Zero, [1]))));
		bool rejected = false;
		await reader.PlayTimedAsync(async (_, _) => {
			await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadAsync()).WaitAsync(TimeSpan.FromSeconds(2)); rejected = true;
		});
		Assert.True(rejected);
	}

	[Fact]
	public async Task Cancellation_during_wait_leaves_event_undispatched() {
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(Recording(PtyRecordingStatus.Complete,
			Output(TimeSpan.FromSeconds(5), [1]))));
		using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(50)); int calls = 0;
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.PlayTimedAsync((_, _) => { calls++; return ValueTask.CompletedTask; }, cancellationToken: cancel.Token));
		Assert.Equal(0, calls);
	}

	[Fact]
	public async Task Callback_exception_propagates_after_delivered_prefix() {
		IOException expected = new("callback failed"); int calls = 0;
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(Recording(PtyRecordingStatus.Complete,
			Output(TimeSpan.Zero, [1]), Output(TimeSpan.Zero, [2]))));
		IOException actual = await Assert.ThrowsAsync<IOException>(() => reader.PlayTimedAsync((_, _) => {
			calls++; return calls == 2 ? ValueTask.FromException(expected) : ValueTask.CompletedTask;
		}));
		Assert.Same(expected, actual); Assert.Equal(2, calls);
	}

	[Fact]
	public async Task Malformed_later_frame_throws_after_delivered_prefix() {
		byte[] bytes = Recording(PtyRecordingStatus.Complete, Output(TimeSpan.Zero, [1]), Output(TimeSpan.Zero, [2]));
		bytes[16 + 16 + 1 + 1] = 1; int calls = 0;
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(bytes));
		await Assert.ThrowsAsync<PtyRecordingFormatException>(() => reader.PlayTimedAsync((_, _) => { calls++; return ValueTask.CompletedTask; }));
		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task Event_past_cap_fails_before_wait_or_callback_without_payload() {
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(Recording(PtyRecordingStatus.Complete,
			Output(TimeSpan.FromSeconds(10), "SECRET"u8.ToArray())))); int calls = 0; Stopwatch elapsed = Stopwatch.StartNew();
		InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.PlayTimedAsync(
			(_, _) => { calls++; return ValueTask.CompletedTask; }, new() { MaxEventElapsed = TimeSpan.FromSeconds(1) }));
		Assert.Equal(0, calls); Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1)); Assert.DoesNotContain("SECRET", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Timed_playback_preserves_source_ownership() {
		MemoryStream defaultSource = new(Recording(PtyRecordingStatus.Complete));
		await using (PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(defaultSource)) await reader.PlayTimedAsync((_, _) => ValueTask.CompletedTask);
		Assert.True(defaultSource.CanRead); defaultSource.Dispose();

		MemoryStream ownedSource = new(Recording(PtyRecordingStatus.Complete));
		await using (PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(ownedSource, new() { LeaveOpen = false }))
			await reader.PlayTimedAsync((_, _) => ValueTask.CompletedTask);
		Assert.False(ownedSource.CanRead);
	}

	private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static (byte Kind, TimeSpan Elapsed, byte[] Payload) Output(TimeSpan elapsed, byte[] payload) => (1, elapsed, payload);
	private static (byte Kind, TimeSpan Elapsed, byte[] Payload) Resize(TimeSpan elapsed, PtySize size) {
		byte[] payload = new byte[4]; BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)size.Columns); BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)size.Rows);
		return (2, elapsed, payload);
	}

	private static byte[] Recording(PtyRecordingStatus status, params (byte Kind, TimeSpan Elapsed, byte[] Payload)[] events) {
		using MemoryStream stream = new(); stream.Write([0x49, 0x63, 0x6f, 0x64, 0x50, 0x74, 0x79, 0x00, 0x01, 0x00, 0x10, 0x00, 0x50, 0x00, 0x18, 0x00]);
		foreach ((byte kind, TimeSpan elapsed, byte[] payload) in events) WriteFrame(stream, kind, elapsed, payload);
		byte terminal = status switch { PtyRecordingStatus.Complete => 3, PtyRecordingStatus.Truncated => 4, PtyRecordingStatus.Stopped => 5, _ => throw new ArgumentOutOfRangeException(nameof(status)) };
		WriteFrame(stream, terminal, events.Length == 0 ? TimeSpan.Zero : events[^1].Elapsed, []); return stream.ToArray();
	}

	private static void WriteFrame(Stream stream, byte kind, TimeSpan elapsed, byte[] payload) {
		Span<byte> header = stackalloc byte[16]; header[0] = kind; BinaryPrimitives.WriteInt64LittleEndian(header[4..], elapsed.Ticks);
		BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)payload.Length); stream.Write(header); stream.Write(payload);
	}
}
