using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyStartupTests {
	[Fact]
	public async Task Precancelled_start_does_not_invoke_factory() {
		using CancellationTokenSource cancel = new(); cancel.Cancel(); int calls = 0;
		OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PtyProcess.StartCoreAsync(ControlledBackend.Launch(), cancel.Token, (_, _) => { calls++; return Task.FromResult<IPtyBackend>(new ControlledBackend()); }));
		Assert.Equal(cancel.Token, error.CancellationToken); Assert.Equal(0, calls);
	}
	[Fact]
	public async Task Cancellation_after_creation_disposes_before_completion() {
		using CancellationTokenSource cancel = new(); ControlledBackend backend = new();
		TaskCompletionSource<IPtyBackend> created = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<PtyProcess> pending = PtyProcess.StartCoreAsync(ControlledBackend.Launch(), cancel.Token, (_, _) => created.Task);
		cancel.Cancel(); Assert.False(pending.IsCompleted); created.SetResult(backend);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
		Assert.Equal(1, backend.DisposeCount);
	}
	[Fact]
	public async Task Successful_start_transfers_ownership() {
		using CancellationTokenSource cancel = new(); ControlledBackend backend = new();
		PtyProcess process = await backend.Start(cancel.Token); cancel.Cancel();
		Assert.Equal(0, backend.DisposeCount); Assert.False(process.HasExited);
		process.Dispose(); Assert.Equal(1, backend.DisposeCount);
	}
	[Fact]
	public async Task Factory_failure_preserves_original_exception() {
		IOException expected = new("creation failed");
		IOException actual = await Assert.ThrowsAsync<IOException>(() => PtyProcess.StartCoreAsync(ControlledBackend.Launch(), default, (_, _) => Task.FromException<IPtyBackend>(expected)));
		Assert.Same(expected, actual);
	}
	[Fact]
	public async Task Public_start_uses_captured_arguments_and_environment() {
		PtyStartInfo info = PtyTestSupport.Child("captured-argument"); info.Environment["ICOD_PTY_TEST_VALUE"] = "captured-value";
		Task<PtyProcess> startup = PtyProcess.StartAsync(info);
		info.ArgumentList.Clear(); info.Environment["ICOD_PTY_TEST_VALUE"] = "mutated-value";
		await using PtyProcess process = await startup;
		string ready = await PtyTestSupport.ReadUntil(process.Output, "\n");
		Assert.Contains("captured-argument", ready); Assert.Contains("captured-value", ready); Assert.DoesNotContain("mutated-value", ready);
	}
	[Theory]
	[InlineData(false)] [InlineData(true)]
	public async Task Synchronous_and_asynchronous_start_collect_exit(bool asynchronous) {
		await using PtyProcess process = asynchronous ? await PtyProcess.StartAsync(PtyTestSupport.Child("exit")) : PtyProcess.Start(PtyTestSupport.Child("exit"));
		Task<string> drain = PtyTestSupport.Drain(process.Output);
		Assert.Equal(37, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)));
		Assert.Contains("FINAL-MARKER", await drain);
	}
	[Fact]
	public async Task Synchronous_start_does_not_require_synchronization_context_pumping() {
		TaskCompletionSource<int> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
		new Thread(() => {
			SynchronizationContext.SetSynchronizationContext(new NonPumpingContext());
			try { using PtyProcess process = PtyProcess.Start(PtyTestSupport.Child("exit")); finished.SetResult(process.WaitForExitAsync().GetAwaiter().GetResult()); }
			catch (Exception error) { finished.SetException(error); }
		}) { IsBackground = true }.Start();
		Assert.Equal(37, await finished.Task.WaitAsync(TimeSpan.FromSeconds(20)));
	}
	private sealed class NonPumpingContext : SynchronizationContext { public override void Post(SendOrPostCallback d, object? state) { } }
}
