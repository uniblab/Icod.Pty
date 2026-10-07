namespace Icod.Pty.Session;

internal sealed class SessionAutomation {
	private sealed class PendingExpectation {
		internal PendingExpectation(byte[] pattern, int[] prefix, int matchedPrefix) {
			Pattern = pattern;
			Prefix = prefix;
			MatchedPrefix = matchedPrefix;
		}

		internal byte[] Pattern { get; }
		internal int[] Prefix { get; }
		internal int MatchedPrefix { get; set; }
		internal TaskCompletionSource<PtyExpectResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private readonly object gate = new();
	private readonly byte[] buffer;
	private int bufferedCount;
	private bool bufferLimitExceeded;
	private PendingExpectation? pending;
	private PtyExpectStatus? terminalStatus;

	internal SessionAutomation(int maximumBufferedOutputBytes) {
		if (maximumBufferedOutputBytes < PtyAutomationOptions.MinimumBufferedOutputBytes ||
			maximumBufferedOutputBytes > PtyAutomationOptions.MaximumBufferedOutputBytes)
			throw new ArgumentOutOfRangeException(nameof(maximumBufferedOutputBytes));
		buffer = new byte[maximumBufferedOutputBytes];
	}

	internal Task<PtyExpectResult> ExpectAsync(ReadOnlyMemory<byte> pattern, TimeSpan timeout, CancellationToken cancellationToken) {
		AutomationConfiguration.ValidateExpectation(pattern, timeout, buffer.Length);
		byte[] copy = pattern.ToArray();
		PendingExpectation expectation;
		lock (gate) {
			if (pending != null) throw new InvalidOperationException("Only one expectation can be pending for a session.");
			if (bufferLimitExceeded) return Task.FromResult(new PtyExpectResult(PtyExpectStatus.BufferLimitExceeded, 0));

			int[] prefix = BuildPrefix(copy);
			int matchedPrefix = 0;
			for (int index = 0; index < bufferedCount; index++) {
				matchedPrefix = Advance(copy, prefix, matchedPrefix, buffer[index]);
				if (matchedPrefix != copy.Length) continue;

				int consumed = index + 1;
				buffer.AsSpan(consumed, bufferedCount - consumed).CopyTo(buffer);
				bufferedCount -= consumed;
				return Task.FromResult(new PtyExpectResult(PtyExpectStatus.Matched, consumed));
			}
			if (terminalStatus != null) return Task.FromResult(new PtyExpectResult(terminalStatus.Value, 0));

			expectation = new(copy, prefix, matchedPrefix);
			pending = expectation;
		}
		return AwaitAsync(expectation, timeout, cancellationToken);
	}

	internal void Observe(ReadOnlyMemory<byte> output) {
		PendingExpectation? completed = null;
		PtyExpectResult? result = null;
		lock (gate) {
			if (bufferLimitExceeded) return;
			foreach (byte value in output.Span) {
				if (pending != null) {
					pending.MatchedPrefix = Advance(pending.Pattern, pending.Prefix, pending.MatchedPrefix, value);
					if (pending.MatchedPrefix == pending.Pattern.Length) {
						completed = pending;
						result = new(PtyExpectStatus.Matched, bufferedCount + 1L);
						pending = null;
						bufferedCount = 0;
						continue;
					}
				}

				if (bufferedCount < buffer.Length) {
					buffer[bufferedCount++] = value;
					continue;
				}

				bufferLimitExceeded = true;
				if (pending != null) {
					completed = pending;
					result = new(PtyExpectStatus.BufferLimitExceeded, 0);
					pending = null;
				}
				break;
			}
		}
		if (completed != null) completed.Completion.TrySetResult(result!);
	}

	internal void Complete(PtySessionOutputStatus status) {
		PendingExpectation? completed;
		PtyExpectStatus mapped = status switch {
			PtySessionOutputStatus.EndOfStream => PtyExpectStatus.OutputEnded,
			PtySessionOutputStatus.Stopped => PtyExpectStatus.OutputStopped,
			PtySessionOutputStatus.TimedOut => PtyExpectStatus.OutputTimedOut,
			PtySessionOutputStatus.Faulted => PtyExpectStatus.OutputFaulted,
			_ => PtyExpectStatus.OutputFaulted
		};
		lock (gate) {
			if (terminalStatus != null) return;
			terminalStatus = mapped;
			completed = pending;
			pending = null;
		}
		completed?.Completion.TrySetResult(new(mapped, 0));
	}

	private async Task<PtyExpectResult> AwaitAsync(PendingExpectation expectation, TimeSpan timeout, CancellationToken cancellationToken) {
		try {
			return await expectation.Completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
		} catch (TimeoutException) {
			lock (gate) {
				if (ReferenceEquals(pending, expectation)) {
					pending = null;
					return new(PtyExpectStatus.TimedOut, 0);
				}
			}
			return await expectation.Completion.Task.ConfigureAwait(false);
		} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			lock (gate) {
				if (ReferenceEquals(pending, expectation)) {
					pending = null;
					throw;
				}
			}
			return await expectation.Completion.Task.ConfigureAwait(false);
		}
	}

	private static int Advance(byte[] pattern, int[] prefix, int matchedPrefix, byte value) {
		while (matchedPrefix > 0 && pattern[matchedPrefix] != value) matchedPrefix = prefix[matchedPrefix - 1];
		if (pattern[matchedPrefix] == value) matchedPrefix++;
		return matchedPrefix;
	}

	private static int[] BuildPrefix(byte[] pattern) {
		int[] prefix = new int[pattern.Length];
		for (int index = 1, matched = 0; index < pattern.Length; index++) {
			while (matched > 0 && pattern[matched] != pattern[index]) matched = prefix[matched - 1];
			if (pattern[matched] == pattern[index]) matched++;
			prefix[index] = matched;
		}
		return prefix;
	}
}
