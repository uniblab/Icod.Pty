using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtySessionCompletionTests {
	[Fact]
	public async Task Primary_exit_waits_for_final_bytes_and_flush() {
		using FeedStream source = new(); using GateFlushStream destination = new(); ControlledBackend backend = new() { Output = source };
		PtySession session = await SessionTestSupport.Start(backend, destination);
		try {
			backend.Completion.SetResult(37); Assert.False(session.Completion.IsCompleted);
			source.Feed("final-marker"u8.ToArray()); source.End();
			await destination.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(session.Completion.IsCompleted);
			destination.Release.SetResult(); PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.Equal(PtySessionEndReason.PrimaryExited, result.Reason); Assert.Equal(37, result.ExitCode);
			Assert.Equal(PtySessionOutputStatus.EndOfStream, result.OutputStatus); Assert.Equal("final-marker"u8.ToArray(), destination.ToArray());
			Assert.Equal(1, backend.DisposeCount);
		} finally { destination.Release.TrySetResult(); source.End(); await session.DisposeAsync(); }
	}
	[Fact]
	public async Task Drain_expiry_is_distinct_from_EOF_and_releases_process() {
		using FeedStream source = new(); using MemoryStream destination = new(); ControlledBackend backend = new() { Output = source };
		await using PtySession session = await SessionTestSupport.Start(backend, destination, drain: TimeSpan.FromMilliseconds(25));
		backend.Completion.SetResult(37); PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(PtySessionEndReason.PrimaryExited, result.Reason); Assert.Equal(37, result.ExitCode);
		Assert.Equal(PtySessionOutputStatus.TimedOut, result.OutputStatus); Assert.Equal(result.OutputStatus, await session.OutputCompletion); Assert.Equal(1, backend.DisposeCount);
	}
	[Fact]
	public async Task Disposal_interrupts_drain_and_keeps_first_trigger() {
		using FeedStream source = new(); using MemoryStream destination = new(); ControlledBackend backend = new() { Output = source };
		PtySession session = await SessionTestSupport.Start(backend, destination, drain: TimeSpan.FromMinutes(1));
		backend.Completion.SetResult(37); await SessionTestSupport.Until(() => session.GetDiagnostics().Phase == PtySessionPhase.Draining);
		await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(PtySessionEndReason.PrimaryExited, (await session.Completion).Reason); Assert.Equal(PtySessionOutputStatus.Stopped, await session.OutputCompletion);
	}
	[Fact]
	public async Task Cleanup_once_preserves_all_independently_observed_failures() {
		IOException inputError = new("source"), processError = new("process"), outputError = new("destination"), cleanupError = new("cleanup");
		using GateReadStream source = new() { Failure = inputError, IgnoreCancellation = true };
		using GateWriteStream destination = new() { Failure = outputError, IgnoreCancellation = true };
		ControlledBackend backend = new() { Output = new MemoryStream(new byte[1]), DisposeFailure = cleanupError };
		PtySession session = await SessionTestSupport.Start(backend, destination, source);
		try {
			await Task.WhenAll(source.Entered.Task, destination.Entered.Task).WaitAsync(TimeSpan.FromSeconds(5)); backend.Completion.SetException(processError);
			await SessionTestSupport.Until(() => session.GetDiagnostics().Phase == PtySessionPhase.Releasing);
			source.Release.SetResult(); destination.Release.SetResult(); PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.Contains(result.Failures, f => f.Stage == PtySessionFailureStage.Process && ReferenceEquals(f.Exception, processError));
			Assert.Contains(result.Failures, f => f.Stage == PtySessionFailureStage.Input && ReferenceEquals(f.Exception, inputError));
			Assert.Contains(result.Failures, f => f.Stage == PtySessionFailureStage.Output && ReferenceEquals(f.Exception, outputError));
			Assert.Contains(result.Failures, f => f.Stage == PtySessionFailureStage.Cleanup && ReferenceEquals(f.Exception, cleanupError));
			await Assert.ThrowsAsync<AggregateException>(async () => await session.DisposeAsync()); Assert.Equal(1, backend.DisposeCount);
		} finally { source.Release.TrySetResult(); destination.Release.TrySetResult(); try { await session.DisposeAsync(); } catch (AggregateException) { } }
	}
	[Fact]
	public async Task Concurrent_disposal_closes_owned_streams_exactly_once() {
		TrackingStream input = new(), output = new(); ControlledBackend backend = new(); PtySession session = await SessionTestSupport.Start(backend, output, input, leaveOpen: false);
		await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => session.DisposeAsync().AsTask())); session.Dispose();
		Assert.Equal(1, backend.DisposeCount); Assert.Equal(1, input.Disposals); Assert.Equal(1, output.Disposals);
		Assert.Equal(42, session.ProcessId); Assert.Equal(new PtySize(80, 24), session.Size); Assert.Empty((await session.Completion).Failures);
	}
	[Fact]
	public async Task Cancelling_completion_wait_has_no_lifetime_effect() {
		using MemoryStream output = new(); ControlledBackend backend = new(); await using PtySession session = await SessionTestSupport.Start(backend, output);
		using CancellationTokenSource stop = new(); Task wait = session.Completion.WaitAsync(stop.Token); stop.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait); await session.WriteAsync("alive"u8.ToArray());
		Assert.Equal(0, backend.DisposeCount); Assert.Equal(0, backend.TerminateCount);
	}
	[Fact]
	public async Task Input_failure_discovered_while_draining_starts_release_immediately() {
		IOException failure = new("late input failure");
		using GateReadStream input = new() { IgnoreCancellation = true, Failure = failure };
		using GateReadStream output = new() { IgnoreCancellation = true }; using MemoryStream destination = new();
		ControlledBackend backend = new() { Output = output }; PtySession session = await SessionTestSupport.Start(backend, destination, input, TimeSpan.FromMinutes(1));
		try {
			await Task.WhenAll(input.Entered.Task, output.Entered.Task).WaitAsync(TimeSpan.FromSeconds(5));
			backend.Completion.SetResult(37); await SessionTestSupport.Until(() => session.GetDiagnostics().Phase == PtySessionPhase.Draining);
			input.Release.SetResult(); await SessionTestSupport.Until(() => backend.DisposeCount == 1);
			output.Release.SetResult(); PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.Equal(PtySessionEndReason.PrimaryExited, result.Reason); Assert.Equal(PtySessionOutputStatus.Stopped, result.OutputStatus);
			Assert.Contains(result.Failures, item => item.Stage == PtySessionFailureStage.Input && ReferenceEquals(item.Exception, failure));
		} finally {
			input.Release.TrySetResult(); output.Release.TrySetResult();
			try { await session.DisposeAsync(); } catch (AggregateException) { }
		}
	}
	[Fact]
	public async Task Unrelated_cancellation_during_release_is_preserved_as_output_failure() {
		using CancellationTokenSource unrelated = new(); unrelated.Cancel();
		OperationCanceledException failure = new(unrelated.Token);
		using GateWriteStream destination = new() { IgnoreCancellation = true, Failure = failure };
		ControlledBackend backend = new() { Output = new MemoryStream(new byte[1]) }; PtySession session = await SessionTestSupport.Start(backend, destination);
		try {
			await destination.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Task dispose = session.DisposeAsync().AsTask();
			await SessionTestSupport.Until(() => session.GetDiagnostics().Phase == PtySessionPhase.Releasing); destination.Release.SetResult();
			await Assert.ThrowsAsync<AggregateException>(() => dispose);
			PtySessionResult result = await session.Completion;
			Assert.Equal(PtySessionOutputStatus.Faulted, result.OutputStatus);
			PtySessionFailure retained = Assert.Single(result.Failures, item => item.Stage == PtySessionFailureStage.Output);
			Assert.Same(failure, retained.Exception); Assert.Equal(unrelated.Token, ((OperationCanceledException)retained.Exception).CancellationToken);
		} finally {
			destination.Release.TrySetResult();
			try { await session.DisposeAsync(); } catch (AggregateException) { }
		}
	}
}
