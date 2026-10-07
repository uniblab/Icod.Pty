using Icod.Pty.Recording;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class TimedPlaybackSchedulerTests {
	[Fact]
	public async Task First_event_waits_from_origin() {
		FakeClock clock = new(); TimedPlaybackScheduler scheduler = clock.CreateScheduler();
		await scheduler.WaitUntilAsync(TimeSpan.FromMilliseconds(40), default);
		Assert.Equal([TimeSpan.FromMilliseconds(40)], clock.Waits);
	}

	[Fact]
	public async Task Equal_timestamp_keeps_file_order() {
		FakeClock clock = new(); TimedPlaybackScheduler scheduler = clock.CreateScheduler(); List<int> order = [];
		await scheduler.WaitUntilAsync(TimeSpan.FromMilliseconds(20), default); order.Add(1);
		await scheduler.WaitUntilAsync(TimeSpan.FromMilliseconds(20), default); order.Add(2);
		Assert.Equal([1, 2], order); Assert.Single(clock.Waits);
	}

	[Fact]
	public async Task Slow_callback_uses_absolute_target() {
		FakeClock clock = new(); TimedPlaybackScheduler scheduler = clock.CreateScheduler();
		await scheduler.WaitUntilAsync(TimeSpan.FromMilliseconds(40), default);
		clock.Advance(TimeSpan.FromMilliseconds(60));
		await scheduler.WaitUntilAsync(TimeSpan.FromMilliseconds(50), default);
		Assert.Equal([TimeSpan.FromMilliseconds(40)], clock.Waits);
	}

	[Fact]
	public async Task Late_event_needs_no_wait() {
		FakeClock clock = new(TimeSpan.FromSeconds(2)); TimedPlaybackScheduler scheduler = clock.CreateScheduler();
		await scheduler.WaitUntilAsync(TimeSpan.FromSeconds(1), default);
		Assert.Empty(clock.Waits);
	}

	[Fact]
	public async Task Long_target_is_split_into_bounded_waits() {
		FakeClock clock = new(); TimedPlaybackScheduler scheduler = clock.CreateScheduler();
		await scheduler.WaitUntilAsync(TimeSpan.FromSeconds(65), default);
		Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)], clock.Waits);
	}

	[Fact]
	public async Task Sub_millisecond_remainder_uses_positive_delay_without_overflow() {
		FakeClock clock = new(TimeSpan.MaxValue - TimeSpan.FromTicks(1)); TimedPlaybackScheduler scheduler = clock.CreateScheduler();
		await scheduler.WaitUntilAsync(TimeSpan.MaxValue, default);
		Assert.Equal([TimeSpan.FromMilliseconds(1)], clock.Waits);
	}

	[Fact]
	public async Task Cancellation_during_wait_propagates() {
		using CancellationTokenSource source = new();
		TimedPlaybackScheduler scheduler = new(() => TimeSpan.Zero, (_, token) => { source.Cancel(); return Task.FromCanceled(token); });
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scheduler.WaitUntilAsync(TimeSpan.FromSeconds(1), source.Token));
	}

	[Fact]
	public async Task Cancellation_is_checked_when_target_is_already_due() {
		using CancellationTokenSource source = new(); source.Cancel();
		TimedPlaybackScheduler scheduler = new(() => TimeSpan.FromSeconds(2), (_, _) => Task.CompletedTask);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scheduler.WaitUntilAsync(TimeSpan.FromSeconds(1), source.Token));
	}

	private sealed class FakeClock(TimeSpan? initial = null) {
		private TimeSpan elapsed = initial ?? TimeSpan.Zero;
		internal List<TimeSpan> Waits { get; } = [];
		internal void Advance(TimeSpan value) => elapsed = AddSaturating(elapsed, value);
		internal TimedPlaybackScheduler CreateScheduler() => new(() => elapsed, (delay, token) => {
			token.ThrowIfCancellationRequested(); Waits.Add(delay); Advance(delay); return Task.CompletedTask;
		});
		private static TimeSpan AddSaturating(TimeSpan left, TimeSpan right) => left.Ticks > TimeSpan.MaxValue.Ticks - right.Ticks
			? TimeSpan.MaxValue : left + right;
	}
}
