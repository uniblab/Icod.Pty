namespace Icod.Pty.Session;

internal static class SessionPumps {
	internal static async Task InputAsync(Stream source, SessionWriter writer, CancellationToken token, Action<Exception> failed) {
		byte[] buffer = new byte[16384];
		try {
			while (true) {
				int count = await source.ReadAsync(buffer, token).ConfigureAwait(false);
				if (count == 0) return;
				try { await writer.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); }
				catch (InvalidOperationException) when (token.IsCancellationRequested) { return; }
				catch (Exception error) when (token.IsCancellationRequested && error is IOException or ObjectDisposedException) { return; }
			}
		} catch (OperationCanceledException) when (token.IsCancellationRequested) { }
		catch (Exception error) { failed(error); }
	}
	internal static async Task<PtySessionOutputStatus> OutputAsync(Stream source, Stream destination,
		CancellationToken token, Action<Exception> failed) {
		byte[] buffer = new byte[16384];
		try {
			while (true) {
				int count;
				try { count = await source.ReadAsync(buffer, token).ConfigureAwait(false); }
				catch (Exception error) when (token.IsCancellationRequested && error is IOException or ObjectDisposedException) { return PtySessionOutputStatus.Stopped; }
				if (count == 0) {
					await destination.FlushAsync(token).ConfigureAwait(false);
					return PtySessionOutputStatus.EndOfStream;
				}
				await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
			}
		} catch (OperationCanceledException) when (token.IsCancellationRequested) { return PtySessionOutputStatus.Stopped; }
		catch (Exception error) { failed(error); return PtySessionOutputStatus.Faulted; }
	}
}
