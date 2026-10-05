using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtySessionPumpTests {
	[Fact]
	public async Task Input_EOF_leaves_explicit_input_and_primary_alive() {
		using MemoryStream source = new("ABC"u8.ToArray()), destination = new(); ControlledBackend backend = new();
		await using PtySession session = await SessionTestSupport.Start(backend, destination, source);
		await SessionTestSupport.Until(() => ((MemoryStream)backend.Input).Length == 3);
		await session.WriteAsync("DEF"u8.ToArray());
		Assert.Equal("ABCDEF"u8.ToArray(), ((MemoryStream)backend.Input).ToArray()); Assert.False(session.HasExited);
	}
	[Fact]
	public async Task Output_EOF_flushes_exact_bytes_without_completing_session() {
		byte[] bytes = [0, 27, 91, 51, 109, 0xc3, 0xa9, 255]; using TrackingStream destination = new();
		ControlledBackend backend = new() { Output = new MemoryStream(bytes) };
		await using PtySession session = await SessionTestSupport.Start(backend, destination);
		Assert.Equal(PtySessionOutputStatus.EndOfStream, await session.OutputCompletion.WaitAsync(TimeSpan.FromSeconds(5)));
		Assert.Equal(bytes, destination.ToArray()); Assert.Equal(1, destination.Flushes); Assert.False(session.Completion.IsCompleted);
	}
	[Fact]
	public async Task Destination_backpressure_allows_only_one_chunk_ahead() {
		using GateWriteStream destination = new(); MemoryStream source = new(new byte[65536]);
		ControlledBackend backend = new() { Output = source };
		await using PtySession session = await SessionTestSupport.Start(backend, destination);
		await destination.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(16384, source.Position); destination.Release.SetResult();
		Assert.Equal(PtySessionOutputStatus.EndOfStream, await session.OutputCompletion); Assert.Equal(65536, destination.Length);
	}
	[Fact]
	public async Task Flush_failure_is_retained_as_output_failure() {
		IOException failure = new("flush"); using TrackingStream destination = new() { FlushFailure = failure };
		PtySession session = await SessionTestSupport.Start(new(), destination);
		Assert.Equal(PtySessionOutputStatus.Faulted, await session.OutputCompletion);
		PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Contains(result.Failures, item => item.Stage == PtySessionFailureStage.Output && ReferenceEquals(item.Exception, failure));
		await Assert.ThrowsAsync<AggregateException>(async () => await session.DisposeAsync());
	}
	[Fact]
	public async Task Uncooperative_destination_is_joined_before_completion() {
		using GateWriteStream destination = new() { IgnoreCancellation = true };
		PtySession session = await SessionTestSupport.Start(new() { Output = new MemoryStream(new byte[1]) }, destination);
		try {
			await destination.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Task dispose = session.DisposeAsync().AsTask();
			Assert.False(dispose.IsCompleted); Assert.False(session.Completion.IsCompleted); Assert.Equal(1, destination.Active);
			destination.Release.TrySetResult(); await dispose.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(0, destination.Active);
		} finally { destination.Release.TrySetResult(); await session.DisposeAsync(); }
	}
}
