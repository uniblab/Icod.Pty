namespace Icod.Pty.Session;

internal static class SessionPumps {
	internal static async Task InputAsync(Stream source, SessionWriter writer, CancellationToken token, Action<Exception> failed, Action<int>? read = null) {
		byte[] buffer = new byte[16384];
		try {
			while (true) {
				int count = await source.ReadAsync(buffer, token).ConfigureAwait(false);
				read?.Invoke(count);
				if (count == 0) return;
				try { await writer.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); }
				catch (InvalidOperationException) when (token.IsCancellationRequested) { return; }
				catch (Exception error) when (token.IsCancellationRequested && error is IOException or ObjectDisposedException) { return; }
			}
		} catch (OperationCanceledException error) when (IsExpectedCancellation(error, token)) { }
		catch (Exception error) { failed(error); }
	}
	internal static async Task<PtySessionOutputStatus> OutputAsync(Stream source, Stream destination,
		CancellationToken token, Action<Exception> failed, Action<int>? read = null, Action<int>? written = null,
		Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? recorded = null) {
		byte[] buffer = new byte[16384];
		try {
			while (true) {
				int count;
				try { count = await source.ReadAsync(buffer, token).ConfigureAwait(false); }
				catch (Exception error) when (token.IsCancellationRequested && error is IOException or ObjectDisposedException) { return PtySessionOutputStatus.Stopped; }
				read?.Invoke(count);
				if (count == 0) {
					await destination.FlushAsync(token).ConfigureAwait(false);
					return PtySessionOutputStatus.EndOfStream;
				}
				await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
				if (recorded != null) await recorded(buffer.AsMemory(0, count), token).ConfigureAwait(false);
				written?.Invoke(count);
			}
		} catch (OperationCanceledException error) when (IsExpectedCancellation(error, token)) { return PtySessionOutputStatus.Stopped; }
		catch (Exception error) { failed(error); return PtySessionOutputStatus.Faulted; }
	}
	private static bool IsExpectedCancellation(OperationCanceledException error, CancellationToken token) =>
		token.IsCancellationRequested && error.CancellationToken == token;
}
