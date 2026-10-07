using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyScriptRunnerTests {
	[Fact]
	public async Task Early_prompt_is_available_to_the_first_expect() {
		using MemoryStream destination = new(); ControlledBackend backend = new() { Output = new MemoryStream("prompt> "u8.ToArray()) };
		PtySession session = await SessionTestSupport.Start(backend, destination, automation: new());
		try {
			PtyScriptResult result = await PtyScriptRunner.RunAsync(session,
				[PtyScriptStep.Expect("prompt> "u8.ToArray(), TimeSpan.FromSeconds(1))]);

			Assert.Equal(new(PtyScriptStatus.Completed, 1, null, null), result);
		} finally { backend.Completion.TrySetResult(0); await session.DisposeAsync(); }
	}

	[Fact]
	public async Task Response_arriving_during_send_is_retained_for_the_next_step() {
		using FeedStream source = new(); using MemoryStream destination = new();
		ImmediateResponseStream input = new(source, "response"u8.ToArray());
		ControlledBackend backend = new() { Input = input, Output = source };
		PtySession session = await SessionTestSupport.Start(backend, destination, automation: new());
		try {
			PtyScriptResult result = await PtyScriptRunner.RunAsync(session, [
				PtyScriptStep.Send("request"u8.ToArray()),
				PtyScriptStep.Expect("response"u8.ToArray(), TimeSpan.FromSeconds(1))
			]);

			Assert.Equal(new(PtyScriptStatus.Completed, 2, null, null), result);
			Assert.Equal("request"u8.ToArray(), input.ToArray());
		} finally { source.End(); backend.Completion.TrySetResult(0); await session.DisposeAsync(); }
	}

	[Fact]
	public async Task Consecutive_expects_share_the_retained_suffix() {
		using MemoryStream destination = new(); ControlledBackend backend = new() { Output = new MemoryStream("one-two"u8.ToArray()) };
		PtySession session = await SessionTestSupport.Start(backend, destination, automation: new());
		try {
			PtyScriptResult result = await PtyScriptRunner.RunAsync(session, [
				PtyScriptStep.Expect("one"u8.ToArray(), TimeSpan.FromSeconds(1)),
				PtyScriptStep.Expect("two"u8.ToArray(), TimeSpan.FromSeconds(1))
			]);

			Assert.Equal(new(PtyScriptStatus.Completed, 2, null, null), result);
		} finally { backend.Completion.TrySetResult(0); await session.DisposeAsync(); }
	}

	[Fact]
	public async Task Empty_send_is_a_completed_ordered_step() {
		using FeedStream source = new(); using MemoryStream destination = new(); ControlledBackend backend = new() { Output = source };
		PtySession session = await SessionTestSupport.Start(backend, destination, automation: new());
		try {
			PtyScriptResult result = await PtyScriptRunner.RunAsync(session, [PtyScriptStep.Send(ReadOnlyMemory<byte>.Empty)]);

			Assert.Equal(new(PtyScriptStatus.Completed, 1, null, null), result);
			Assert.Equal(0, backend.Input.Length);
		} finally { source.End(); backend.Completion.TrySetResult(0); await session.DisposeAsync(); }
	}

	[Fact]
	public async Task Timeout_reports_the_failed_index_and_leaves_the_session_owned_by_the_caller() {
		using FeedStream source = new(); using MemoryStream destination = new(); ControlledBackend backend = new() { Output = source };
		PtySession session = await SessionTestSupport.Start(backend, destination, automation: new());
		try {
			source.Feed("ready"u8.ToArray());
			PtyScriptResult result = await PtyScriptRunner.RunAsync(session, [
				PtyScriptStep.Expect("ready"u8.ToArray(), TimeSpan.FromSeconds(1)),
				PtyScriptStep.Expect("missing"u8.ToArray(), TimeSpan.FromMilliseconds(20)),
				PtyScriptStep.Send("never"u8.ToArray())
			]);

			Assert.Equal(new(PtyScriptStatus.ExpectationFailed, 1, 1, PtyExpectStatus.TimedOut), result);
			Assert.False(session.Completion.IsCompleted);
			await session.WriteAsync("still-owned"u8.ToArray());
			Assert.Equal("still-owned"u8.ToArray(), ((MemoryStream)backend.Input).ToArray());
		} finally { source.End(); backend.Completion.TrySetResult(0); await session.DisposeAsync(); }
	}

	[Fact]
	public async Task Cancellation_during_send_is_propagated_and_releases_the_runner_gate() {
		using FeedStream source = new(); using MemoryStream destination = new(); using GateWriteStream input = new();
		ControlledBackend backend = new() { Input = input, Output = source };
		PtySession session = await SessionTestSupport.Start(backend, destination, automation: new());
		try {
			using CancellationTokenSource cancellation = new();
			Task<PtyScriptResult> cancelled = PtyScriptRunner.RunAsync(session, [PtyScriptStep.Send("blocked"u8.ToArray())], cancellation.Token);
			await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();

			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
			input.Release.TrySetResult();
			Assert.Equal(PtyScriptStatus.Completed, (await PtyScriptRunner.RunAsync(session, Array.Empty<PtyScriptStep>())).Status);
		} finally { input.Release.TrySetResult(); source.End(); backend.Completion.TrySetResult(0); await session.DisposeAsync(); }
	}

	[Fact]
	public async Task Cancellation_during_expect_is_propagated_and_releases_the_runner_gate() {
		using FeedStream source = new(); using MemoryStream destination = new(); ControlledBackend backend = new() { Output = source };
		PtySession session = await SessionTestSupport.Start(backend, destination, automation: new());
		try {
			using CancellationTokenSource cancellation = new();
			Task<PtyScriptResult> cancelled = PtyScriptRunner.RunAsync(session,
				[PtyScriptStep.Expect("missing"u8.ToArray(), TimeSpan.FromSeconds(1))], cancellation.Token);
			cancellation.Cancel();

			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
			Assert.Equal(PtyScriptStatus.Completed, (await PtyScriptRunner.RunAsync(session, Array.Empty<PtyScriptStep>())).Status);
		} finally { source.End(); backend.Completion.TrySetResult(0); await session.DisposeAsync(); }
	}

	[Fact]
	public async Task Input_write_failure_is_not_converted_to_a_script_result() {
		IOException failure = new("input"); using FeedStream source = new(); using MemoryStream destination = new();
		using GateWriteStream input = new() { Failure = failure }; input.Release.SetResult();
		ControlledBackend backend = new() { Input = input, Output = source };
		PtySession session = await SessionTestSupport.Start(backend, destination, automation: new());
		try {
			IOException actual = await Assert.ThrowsAsync<IOException>(() =>
				PtyScriptRunner.RunAsync(session, [PtyScriptStep.Send("fail"u8.ToArray())]));

			Assert.Same(failure, actual);
			Assert.Equal(PtySessionEndReason.InputFailed, (await session.Completion.WaitAsync(TimeSpan.FromSeconds(5))).Reason);
		} finally {
			source.End();
			try { await session.DisposeAsync(); } catch (AggregateException) { }
		}
	}

	[Fact]
	public async Task A_second_runner_on_the_same_session_is_rejected() {
		using FeedStream source = new(); using MemoryStream destination = new(); ControlledBackend backend = new() { Output = source };
		PtySession session = await SessionTestSupport.Start(backend, destination, automation: new());
		try {
			Task<PtyScriptResult> first = PtyScriptRunner.RunAsync(session,
				[PtyScriptStep.Expect("first"u8.ToArray(), TimeSpan.FromSeconds(1))]);

			await Assert.ThrowsAsync<InvalidOperationException>(() => PtyScriptRunner.RunAsync(session, Array.Empty<PtyScriptStep>()));
			source.Feed("first"u8.ToArray());
			Assert.Equal(PtyScriptStatus.Completed, (await first).Status);
		} finally { source.End(); backend.Completion.TrySetResult(0); await session.DisposeAsync(); }
	}
}

internal sealed class ImmediateResponseStream(FeedStream output, byte[] response) : MemoryStream {
	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) {
		token.ThrowIfCancellationRequested();
		base.Write(buffer.Span);
		output.Feed(response);
		return ValueTask.CompletedTask;
	}
}
