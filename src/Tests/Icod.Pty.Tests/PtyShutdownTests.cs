using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyShutdownTests {
	private static PtyShutdownOptions Short(bool force = false) => new() { GracePeriod = TimeSpan.FromMilliseconds(80), TerminationTimeout = TimeSpan.FromMilliseconds(80), ForceTermination = force };
	[Fact]
	public async Task Graceful_request_collects_exit() {
		await using PtyProcess process = await PtyProcess.StartAsync(PtyTestSupport.Child());
		await PtyTestSupport.ReadUntil(process.Output, "READY:ok");
		Task<string> drain = PtyTestSupport.Drain(process.Output);
		PtyShutdownResult result = await process.ShutdownAsync(new() { Request = PtyTestSupport.Line("quit") });
		Assert.Equal(PtyShutdownStatus.Exited, result.Status); Assert.Equal(23, result.ExitCode); Assert.False(result.ForcedTerminationRequested);
		Assert.Contains("BYE-MARKER", await drain); Assert.True(process.Output.CanRead);
	}
	[Fact]
	public async Task Timeout_without_force_keeps_session_usable() {
		await using PtyProcess process = await PtyProcess.StartAsync(PtyTestSupport.Child());
		await PtyTestSupport.ReadUntil(process.Output, "READY:ok");
		PtyShutdownResult result = await process.ShutdownAsync(Short());
		Assert.Equal(PtyShutdownStatus.TimedOut, result.Status); Assert.Null(result.ExitCode); Assert.False(result.ForcedTerminationRequested); Assert.False(process.HasExited);
		await process.Input.WriteAsync(PtyTestSupport.Line("still-alive"));
		await PtyTestSupport.ReadUntil(process.Output, "ECHO:still-alive");
	}
	[Fact]
	public async Task Timeout_with_force_collects_exit() {
		await using PtyProcess process = await PtyProcess.StartAsync(PtyTestSupport.Child());
		await PtyTestSupport.ReadUntil(process.Output, "READY:ok");
		Task<string> drain = PtyTestSupport.Drain(process.Output);
		PtyShutdownOptions options = Short(true); options.TerminationTimeout = TimeSpan.FromSeconds(10);
		PtyShutdownResult result = await process.ShutdownAsync(options);
		Assert.Equal(PtyShutdownStatus.Exited, result.Status); Assert.NotNull(result.ExitCode); Assert.True(result.ForcedTerminationRequested); await drain;
	}
	[Fact]
	public async Task Termination_collection_timeout_reports_requested_force() {
		ControlledBackend backend = new() { CompleteOnTerminate = false }; using PtyProcess process = await backend.Start();
		PtyShutdownResult result = await process.ShutdownAsync(Short(true));
		Assert.Equal(PtyShutdownStatus.TimedOut, result.Status); Assert.Null(result.ExitCode); Assert.True(result.ForcedTerminationRequested); Assert.Equal(1, backend.TerminateCount);
	}
	[Fact]
	public async Task Blocked_request_obeys_grace_budget() {
		ControlledBackend backend = new() { Input = new WriteStream(async (_, token) => await Task.Delay(Timeout.Infinite, token)) };
		using PtyProcess process = await backend.Start(); PtyShutdownOptions options = Short(); options.Request = new byte[100];
		PtyShutdownResult result = await process.ShutdownAsync(options).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(PtyShutdownStatus.TimedOut, result.Status); Assert.Equal(0, backend.TerminateCount); Assert.True(process.Output.CanRead);
		backend.Completion.SetResult(7); Assert.Equal(7, (await process.ShutdownAsync(Short())).ExitCode);
	}
	[Fact]
	public async Task Caller_cancellation_never_initiates_force() {
		TaskCompletionSource writing = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ControlledBackend backend = new() { Input = new WriteStream(async (_, token) => { writing.SetResult(); await Task.Delay(Timeout.Infinite, token); }) };
		using PtyProcess process = await backend.Start(); using CancellationTokenSource cancel = new();
		Task<PtyShutdownResult> pending = process.ShutdownAsync(new() { Request = new byte[] { 1 }, ForceTermination = true }, cancel.Token);
		await writing.Task; cancel.Cancel();
		OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
		Assert.Equal(cancel.Token, error.CancellationToken); Assert.Equal(0, backend.TerminateCount); Assert.False(process.HasExited);
		backend.Completion.SetResult(0); Assert.Equal(PtyShutdownStatus.Exited, (await process.ShutdownAsync(Short())).Status);
	}
	[Fact]
	public async Task Concurrent_shutdown_is_rejected() {
		ControlledBackend backend = new(); using PtyProcess process = await backend.Start(); using CancellationTokenSource cancel = new();
		Task<PtyShutdownResult> first = process.ShutdownAsync(new(), cancel.Token);
		await Assert.ThrowsAsync<InvalidOperationException>(() => process.ShutdownAsync(new()));
		cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
		backend.Completion.SetResult(2); Assert.Equal(2, (await process.ShutdownAsync(new())).ExitCode);
	}
	[Fact]
	public async Task Retry_after_timeout_is_allowed() {
		ControlledBackend backend = new(); using PtyProcess process = await backend.Start();
		Assert.Equal(PtyShutdownStatus.TimedOut, (await process.ShutdownAsync(Short())).Status);
		backend.Completion.SetResult(8); Assert.Equal(8, (await process.ShutdownAsync(Short())).ExitCode);
	}
	[Fact]
	public async Task Request_snapshot_is_independent() {
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously); byte[]? captured = null;
		ControlledBackend backend = new();
		backend.Input = new WriteStream(async (bytes, token) => { await release.Task.WaitAsync(token); captured = bytes.ToArray(); backend.Completion.SetResult(9); });
		using PtyProcess process = await backend.Start(); byte[] request = [0x71];
		PtyShutdownOptions options = new() { Request = request }; Task<PtyShutdownResult> pending = process.ShutdownAsync(options);
		request[0] = 0x78; options.Request = new byte[] { 0 }; options.ForceTermination = true; release.SetResult();
		Assert.Equal(9, (await pending).ExitCode); Assert.Equal(new byte[] { 0x71 }, captured); Assert.Equal(0, backend.TerminateCount);
	}
	[Fact]
	public async Task Write_failure_does_not_escalate() {
		ControlledBackend backend = new() { Input = new WriteStream((_, _) => ValueTask.FromException(new IOException("broken input"))) };
		using PtyProcess process = await backend.Start();
		await Assert.ThrowsAsync<IOException>(() => process.ShutdownAsync(new() { Request = new byte[] { 1 }, ForceTermination = true }));
		Assert.Equal(0, backend.TerminateCount);
	}
	[Fact]
	public async Task Already_exited_skips_request() {
		ControlledBackend backend = new(); using PtyProcess process = await backend.Start(); backend.Completion.SetResult(11);
		PtyShutdownResult result = await process.ShutdownAsync(new() { Request = new byte[] { 1 }, ForceTermination = true });
		Assert.Equal(11, result.ExitCode); Assert.False(result.ForcedTerminationRequested); Assert.Equal(0, backend.Input.Length);
	}
	[Theory]
	[InlineData(0)] [InlineData(-1)] [InlineData(2147483648d)]
	public async Task Invalid_deadlines_are_rejected_even_after_exit(double milliseconds) {
		ControlledBackend backend = new(); using PtyProcess process = await backend.Start(); backend.Completion.SetResult(0);
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => process.ShutdownAsync(new() { GracePeriod = TimeSpan.FromMilliseconds(milliseconds) }));
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => process.ShutdownAsync(new() { TerminationTimeout = TimeSpan.FromMilliseconds(milliseconds) }));
	}
	[Fact]
	public async Task Dispose_during_shutdown_keeps_cleanup_owned_by_process() {
		ControlledBackend backend = new(); PtyProcess process = await backend.Start(); Task<PtyShutdownResult> pending = process.ShutdownAsync(new());
		process.Dispose(); PtyShutdownResult result = await pending;
		Assert.Equal(PtyShutdownStatus.Exited, result.Status); Assert.Equal(1, backend.DisposeCount);
		await Assert.ThrowsAsync<ObjectDisposedException>(() => process.ShutdownAsync(new()));
	}
	[Fact]
	public async Task Exit_during_request_failure_returns_collected_status() {
		ControlledBackend backend = new(); backend.Input = new WriteStream((_, _) => { backend.Completion.SetResult(12); throw new IOException("exit raced write"); });
		using PtyProcess process = await backend.Start();
		PtyShutdownResult result = await process.ShutdownAsync(new() { Request = new byte[] { 1 }, ForceTermination = true });
		Assert.Equal(12, result.ExitCode); Assert.False(result.ForcedTerminationRequested);
	}
	[Fact]
	public async Task Graceful_shutdown_preserves_large_final_output() {
		await using PtyProcess process = await PtyProcess.StartAsync(PtyTestSupport.Child("final-output"));
		await PtyTestSupport.ReadUntil(process.Output, "FINAL-READY"); Task<string> drain = PtyTestSupport.Drain(process.Output);
		PtyShutdownResult result = await process.ShutdownAsync(new() { Request = PtyTestSupport.Line("quit"), GracePeriod = TimeSpan.FromSeconds(15) });
		Assert.Equal(23, result.ExitCode); string output = await drain;
		for (int i = 0; i < 1024; i++) Assert.Contains($"FINAL:{i:D4}:", output);
		Assert.Contains("FINAL-END", output);
	}
	private sealed class WriteStream(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> write) : MemoryStream {
		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => write(buffer, cancellationToken);
	}
}
