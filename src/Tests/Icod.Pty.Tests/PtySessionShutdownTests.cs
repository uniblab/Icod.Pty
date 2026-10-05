using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtySessionShutdownTests {
	[Fact]
	public async Task Rejected_shutdown_leaves_normal_input_usable() {
		using MemoryStream destination = new(); ControlledBackend backend = new();
		await using PtySession session = await SessionTestSupport.Start(backend, destination);
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.ShutdownAsync(new() { GracePeriod = TimeSpan.Zero }));
		await Assert.ThrowsAsync<InvalidOperationException>(() => session.ShutdownAsync(new() { TerminationTarget = PtyProcessTarget.OwnedScope }));
		using CancellationTokenSource canceled = new(); canceled.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ShutdownAsync(new(), canceled.Token));
		await session.WriteAsync("usable"u8.ToArray()); Assert.Equal("usable"u8.ToArray(), ((MemoryStream)backend.Input).ToArray());
	}
	[Fact]
	public async Task Accepted_shutdown_seals_permanently_and_allows_sequential_retries() {
		using MemoryStream destination = new(); ControlledBackend backend = new();
		await using PtySession session = await SessionTestSupport.Start(backend, destination);
		PtyShutdownResult first = await session.ShutdownAsync(new() { Request = "quit"u8.ToArray(), GracePeriod = TimeSpan.FromMilliseconds(20) });
		Assert.Equal(PtyShutdownStatus.TimedOut, first.Status);
		await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.WriteAsync("late"u8.ToArray()));
		using CancellationTokenSource stop = new();
		Task<PtyShutdownResult> retry = session.ShutdownAsync(new() { ForceTermination = true }, stop.Token);
		await Assert.ThrowsAsync<InvalidOperationException>(() => session.ShutdownAsync(new())); stop.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry); Assert.Equal(0, backend.TerminateCount);
		await session.DisposeAsync(); Assert.Equal(first, (await session.Completion).LastShutdownResult);
		Assert.Equal("quit"u8.ToArray(), ((MemoryStream)backend.Input).ToArray());
	}
	[Fact]
	public async Task Grace_budget_includes_waiting_for_uncooperative_admitted_write() {
		using MemoryStream destination = new(); using GateWriteStream input = new() { IgnoreCancellation = true, Prefix = 2 };
		ControlledBackend backend = new() { Input = input }; PtySession session = await SessionTestSupport.Start(backend, destination);
		try {
			Task normal = session.WriteAsync("ABCD"u8.ToArray()).AsTask(); await input.Entered.Task;
			PtyShutdownResult result = await session.ShutdownAsync(new() { Request = "quit"u8.ToArray(), GracePeriod = TimeSpan.FromMilliseconds(30) }).WaitAsync(TimeSpan.FromSeconds(5));
			Assert.Equal(PtyShutdownStatus.TimedOut, result.Status); Assert.Equal("AB"u8.ToArray(), input.ToArray());
			input.Release.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => normal);
		} finally { input.Release.TrySetResult(); await session.DisposeAsync(); }
	}
	[Fact]
	public async Task Accepted_shutdown_cancels_the_optional_input_source() {
		using GateReadStream source = new(); using MemoryStream destination = new(); ControlledBackend backend = new();
		PtySession session = await SessionTestSupport.Start(backend, destination, source);
		try {
			await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			PtyShutdownResult result = await session.ShutdownAsync(new() { GracePeriod = TimeSpan.FromMilliseconds(20) });
			Assert.Equal(PtyShutdownStatus.TimedOut, result.Status);
			await source.Finished.Task.WaitAsync(TimeSpan.FromSeconds(1));
			Assert.False(session.Completion.IsCompleted);
		} finally { source.Release.TrySetResult(); await session.DisposeAsync(); }
	}
	[Fact]
	public async Task Primary_exit_while_shutdown_request_is_blocked_returns_exit_result() {
		using MemoryStream destination = new(); using GateWriteStream input = new(); ControlledBackend backend = new() { Input = input };
		PtySession session = await SessionTestSupport.Start(backend, destination);
		try {
			Task<PtyShutdownResult> shutdown = session.ShutdownAsync(new() { Request = "quit"u8.ToArray(), GracePeriod = TimeSpan.FromMinutes(1) });
			await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); backend.Completion.SetResult(12);
			PtyShutdownResult result = await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.Equal(PtyShutdownStatus.Exited, result.Status); Assert.Equal(12, result.ExitCode);
			Assert.Equal(result, (await session.Completion).LastShutdownResult);
		} finally { input.Release.TrySetResult(); await session.DisposeAsync(); }
	}
	[Fact]
	public async Task Shutdown_result_survives_primary_exit_and_joined_disposal() {
		using MemoryStream destination = new(); ControlledBackend backend = new();
		PtySession session = await SessionTestSupport.Start(backend, destination);
		Task<PtyShutdownResult> shutdown = session.ShutdownAsync(new() { ForceTermination = true, GracePeriod = TimeSpan.FromMilliseconds(20) });
		PtyShutdownResult result = await shutdown; Assert.True(result.ForcedTerminationRequested); Assert.Equal(1, backend.TerminateCount);
		await session.DisposeAsync(); Assert.Equal(result, (await session.Completion).LastShutdownResult); Assert.Equal(1, backend.DisposeCount);
	}
}
