using Icod.Pty.Session;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtySessionStartTests {
	[Fact]
	public async Task Captured_options_preserve_stream_ownership_and_defaults() {
		using MemoryStream output = new(), input = new(), replacement = new();
		PtySessionOptions options = new(output) { Input = input, LeaveInputOpen = false, LeaveOutputOpen = false };
		SessionConfiguration captured = SessionConfiguration.Capture(options);
		options.Input = replacement; options.LeaveInputOpen = true; options.LeaveOutputOpen = true; options.DrainTimeout = TimeSpan.FromMinutes(1);
		Assert.Equal(TimeSpan.FromSeconds(5), captured.DrainTimeout);
		ControlledBackend backend = new();
		await using PtySession session = await PtySession.StartCoreAsync(ControlledBackend.Launch(), captured, default, (_, token) => backend.Start(token));
		Assert.Equal(PtyProcessOwnership.PrimaryProcess, session.Ownership);
		await session.DisposeAsync();
		Assert.False(input.CanRead); Assert.False(output.CanWrite); Assert.True(replacement.CanRead);
	}
	[Theory]
	[InlineData(0)] [InlineData(-1)] [InlineData(2147483648d)]
	public void Invalid_drain_budget_is_rejected_before_launch(double milliseconds) {
		using MemoryStream output = new();
		Assert.Throws<ArgumentOutOfRangeException>(() => { _ = PtySession.StartAsync(new("must-not-launch"), new(output) { DrainTimeout = TimeSpan.FromMilliseconds(milliseconds) }); });
	}
	[Fact]
	public void Invalid_streams_are_rejected_before_launch() {
		using MemoryStream stream = new();
		Assert.Throws<ArgumentException>(() => { _ = PtySession.StartAsync(new("must-not-launch"), new(stream) { Input = stream }); });
		using MemoryStream readOnly = new([], false);
		Assert.Throws<ArgumentException>(() => { _ = PtySession.StartAsync(new("must-not-launch"), new(readOnly)); });
		MemoryStream disposed = new(); disposed.Dispose();
		Assert.Throws<ArgumentException>(() => { _ = PtySession.StartAsync(new("must-not-launch"), new(stream) { Input = disposed }); });
	}
	[Fact]
	public async Task Precancelled_start_never_invokes_factory_or_takes_streams() {
		using MemoryStream output = new(); using CancellationTokenSource stop = new(); stop.Cancel();
		int calls = 0;
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PtySession.StartCoreAsync(ControlledBackend.Launch(),
			SessionConfiguration.Capture(new(output) { LeaveOutputOpen = false }), stop.Token, (_, _) => { calls++; throw new IOException(); }));
		Assert.Equal(0, calls); Assert.True(output.CanWrite);
	}
	[Fact]
	public async Task Cancellation_after_child_creation_collects_child_but_leaves_supplied_streams() {
		using MemoryStream output = new(), input = new(); using CancellationTokenSource stop = new(); ControlledBackend backend = new();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PtySession.StartCoreAsync(ControlledBackend.Launch(),
			SessionConfiguration.Capture(new(output) { Input = input, LeaveInputOpen = false, LeaveOutputOpen = false }), stop.Token,
			async (_, _) => { PtyProcess process = await backend.Start(); stop.Cancel(); return process; }));
		Assert.Equal(1, backend.DisposeCount); Assert.True(output.CanWrite); Assert.True(input.CanRead);
	}
	[Fact]
	public async Task Setup_and_rollback_failures_preserve_original_and_leave_external_streams_open() {
		using MemoryStream output = new(); IOException setup = new("setup"), cleanup = new("cleanup");
		ControlledBackend backend = new() { OutputAccessFailure = setup, DisposeFailure = cleanup };
		AggregateException error = await Assert.ThrowsAsync<AggregateException>(() => PtySession.StartCoreAsync(ControlledBackend.Launch(),
			SessionConfiguration.Capture(new(output) { LeaveOutputOpen = false }), default, (_, token) => backend.Start(token)));
		Assert.Same(setup, error.InnerExceptions[0]); Assert.Same(cleanup, error.InnerExceptions[1]);
		Assert.Equal(1, backend.DisposeCount); Assert.True(output.CanWrite);
	}
}
