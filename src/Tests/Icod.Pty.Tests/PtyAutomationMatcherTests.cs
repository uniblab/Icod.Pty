using Icod.Pty.Session;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyAutomationMatcherTests {
	[Fact]
	public async Task Matches_binary_bytes_without_decoding() {
		SessionAutomation automation = new(32);
		automation.Observe(new byte[] { 0xff, 0x00, 0xfe, 0x80 });

		PtyExpectResult result = await automation.ExpectAsync(new byte[] { 0x00, 0xfe }, TimeSpan.FromSeconds(1), default);

		Assert.Equal(new(PtyExpectStatus.Matched, 3), result);
	}

	[Fact]
	public async Task Finds_overlapping_pattern_across_one_byte_chunks() {
		SessionAutomation automation = new(32);
		Task<PtyExpectResult> pending = automation.ExpectAsync("aab"u8.ToArray(), TimeSpan.FromSeconds(1), default);

		foreach (byte value in "aaab"u8) automation.Observe(new[] { value });

		Assert.Equal(new(PtyExpectStatus.Matched, 4), await pending);
	}

	[Fact]
	public async Task Consecutive_expectations_share_the_unconsumed_suffix() {
		SessionAutomation automation = new(64);
		automation.Observe("junk:first:second:tail"u8.ToArray());

		PtyExpectResult first = await automation.ExpectAsync("first"u8.ToArray(), TimeSpan.FromSeconds(1), default);
		PtyExpectResult second = await automation.ExpectAsync("second"u8.ToArray(), TimeSpan.FromSeconds(1), default);

		Assert.Equal(new(PtyExpectStatus.Matched, 10), first);
		Assert.Equal(new(PtyExpectStatus.Matched, 7), second);
	}

	[Fact]
	public async Task Output_observed_before_the_wait_is_retained() {
		SessionAutomation automation = new(32);
		automation.Observe("prompt> "u8.ToArray());

		Assert.Equal(new(PtyExpectStatus.Matched, 8),
			await automation.ExpectAsync("prompt> "u8.ToArray(), TimeSpan.FromSeconds(1), default));
	}

	[Fact]
	public async Task Timeout_preserves_the_cursor() {
		SessionAutomation automation = new(32);
		automation.Observe("ab"u8.ToArray());

		PtyExpectResult timedOut = await automation.ExpectAsync("abc"u8.ToArray(), TimeSpan.FromMilliseconds(20), default);
		automation.Observe("c"u8.ToArray());

		Assert.Equal(new(PtyExpectStatus.TimedOut, 0), timedOut);
		Assert.Equal(new(PtyExpectStatus.Matched, 3),
			await automation.ExpectAsync("abc"u8.ToArray(), TimeSpan.FromSeconds(1), default));
	}

	[Fact]
	public async Task Cancellation_preserves_the_cursor() {
		SessionAutomation automation = new(32);
		automation.Observe("ab"u8.ToArray());
		using CancellationTokenSource cancellation = new();
		Task<PtyExpectResult> cancelled = automation.ExpectAsync("abc"u8.ToArray(), TimeSpan.FromSeconds(1), cancellation.Token);

		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
		automation.Observe("c"u8.ToArray());

		Assert.Equal(new(PtyExpectStatus.Matched, 3),
			await automation.ExpectAsync("abc"u8.ToArray(), TimeSpan.FromSeconds(1), default));
	}

	[Fact]
	public async Task Pattern_is_copied_before_awaiting_output() {
		SessionAutomation automation = new(32);
		byte[] pattern = "ready"u8.ToArray();
		Task<PtyExpectResult> pending = automation.ExpectAsync(pattern, TimeSpan.FromSeconds(1), default);

		pattern[0] = (byte)'x';
		automation.Observe("ready"u8.ToArray());

		Assert.Equal(PtyExpectStatus.Matched, (await pending).Status);
	}

	[Fact]
	public async Task A_second_pending_expectation_is_rejected() {
		SessionAutomation automation = new(32);
		Task<PtyExpectResult> first = automation.ExpectAsync("first"u8.ToArray(), TimeSpan.FromSeconds(1), default);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			automation.ExpectAsync("second"u8.ToArray(), TimeSpan.FromSeconds(1), default));
		automation.Observe("first"u8.ToArray());

		Assert.Equal(PtyExpectStatus.Matched, (await first).Status);
	}

	[Fact]
	public async Task Exact_buffer_limit_is_matchable() {
		SessionAutomation automation = new(4);
		automation.Observe("abcd"u8.ToArray());

		Assert.Equal(new PtyExpectResult(PtyExpectStatus.Matched, 4),
			await automation.ExpectAsync("cd"u8.ToArray(), TimeSpan.FromSeconds(1), default));
	}

	[Fact]
	public async Task One_byte_past_the_limit_latches_overrun() {
		SessionAutomation automation = new(4);
		Task<PtyExpectResult> pending = automation.ExpectAsync(new byte[] { 9 }, TimeSpan.FromSeconds(1), default);

		automation.Observe(new byte[] { 1, 2 });
		automation.Observe(new byte[] { 3, 4 });
		automation.Observe(new byte[] { 5 });
		PtyExpectResult first = await pending;
		automation.Observe(Enumerable.Repeat((byte)6, 100).ToArray());

		Assert.Equal(new(PtyExpectStatus.BufferLimitExceeded, 0), first);
		Assert.Equal(new(PtyExpectStatus.BufferLimitExceeded, 0),
			await automation.ExpectAsync(new byte[] { 6 }, TimeSpan.FromSeconds(1), default));
	}

	[Fact]
	public async Task Buffered_match_wins_over_later_output_completion() {
		SessionAutomation automation = new(32);
		automation.Observe("ready"u8.ToArray());
		automation.Complete(PtySessionOutputStatus.EndOfStream);

		Assert.Equal(new(PtyExpectStatus.Matched, 5),
			await automation.ExpectAsync("ready"u8.ToArray(), TimeSpan.FromSeconds(1), default));
		Assert.Equal(new(PtyExpectStatus.OutputEnded, 0),
			await automation.ExpectAsync(new byte[] { 1 }, TimeSpan.FromSeconds(1), default));
	}

	[Theory]
	[InlineData(PtySessionOutputStatus.EndOfStream, PtyExpectStatus.OutputEnded)]
	[InlineData(PtySessionOutputStatus.Stopped, PtyExpectStatus.OutputStopped)]
	[InlineData(PtySessionOutputStatus.TimedOut, PtyExpectStatus.OutputTimedOut)]
	[InlineData(PtySessionOutputStatus.Faulted, PtyExpectStatus.OutputFaulted)]
	public async Task Output_completion_settles_current_and_future_waits(PtySessionOutputStatus output, PtyExpectStatus expected) {
		SessionAutomation during = new(32);
		Task<PtyExpectResult> pending = during.ExpectAsync("missing"u8.ToArray(), TimeSpan.FromSeconds(1), default);
		during.Complete(output);

		Assert.Equal(new(expected, 0), await pending);
		Assert.Equal(new(expected, 0),
			await during.ExpectAsync("later"u8.ToArray(), TimeSpan.FromSeconds(1), default));

		SessionAutomation before = new(32);
		before.Complete(output);
		Assert.Equal(new(expected, 0),
			await before.ExpectAsync("later"u8.ToArray(), TimeSpan.FromSeconds(1), default));
	}

	[Fact]
	public async Task Match_won_before_cancellation_remains_the_result() {
		SessionAutomation automation = new(32);
		using CancellationTokenSource cancellation = new();
		Task<PtyExpectResult> pending = automation.ExpectAsync("ok"u8.ToArray(), TimeSpan.FromSeconds(1), cancellation.Token);

		automation.Observe("ok"u8.ToArray());
		cancellation.Cancel();

		Assert.Equal(PtyExpectStatus.Matched, (await pending).Status);
	}

	[Fact]
	public async Task Timeout_releases_the_waiter_for_terminal_observation() {
		SessionAutomation automation = new(32);
		Assert.Equal(PtyExpectStatus.TimedOut,
			(await automation.ExpectAsync("missing"u8.ToArray(), TimeSpan.FromMilliseconds(20), default)).Status);

		automation.Complete(PtySessionOutputStatus.Stopped);

		Assert.Equal(PtyExpectStatus.OutputStopped,
			(await automation.ExpectAsync("later"u8.ToArray(), TimeSpan.FromSeconds(1), default)).Status);
	}
}
