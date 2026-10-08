namespace Icod.Pty.Recording;

internal sealed class TimedPlaybackScheduler {
	private static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan MinimumWait = TimeSpan.FromMilliseconds(1);
	private readonly Func<TimeSpan> elapsed;
	private readonly Func<TimeSpan, CancellationToken, Task> delay;

	internal TimedPlaybackScheduler(Func<TimeSpan> elapsed, Func<TimeSpan, CancellationToken, Task> delay) {
		this.elapsed = elapsed; this.delay = delay;
	}

	internal async Task WaitUntilAsync(TimeSpan target, CancellationToken cancellationToken) {
		while (true) {
			cancellationToken.ThrowIfCancellationRequested();
			TimeSpan current = elapsed();
			if (current >= target) return;
			long remainingTicks = target.Ticks - current.Ticks;
			TimeSpan wait = remainingTicks >= MaximumWait.Ticks ? MaximumWait : TimeSpan.FromTicks(remainingTicks);
			if (wait < MinimumWait) wait = MinimumWait;
			await delay(wait, cancellationToken).ConfigureAwait(false);
		}
	}
}
