namespace Icod.Pty;

internal static class ShutdownCoordinator {
	internal static async Task<PtyShutdownResult> RunAsync(IPtyBackend backend, byte[] request,
		TimeSpan gracePeriod, bool forceTermination, TimeSpan terminationTimeout, CancellationToken cancellationToken, PtyProcessTarget target = PtyProcessTarget.PrimaryProcess) {
		cancellationToken.ThrowIfCancellationRequested();
		if (backend.Exit.IsCompletedSuccessfully) return Collected(backend, false);
		using (CancellationTokenSource grace = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
			grace.CancelAfter(gracePeriod);
			try {
				if (request.Length != 0) await backend.Input.WriteAsync(request, grace.Token).ConfigureAwait(false);
				int code = await backend.Exit.WaitAsync(grace.Token).ConfigureAwait(false);
				return new(PtyShutdownStatus.Exited, code, false);
			} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
				throw new OperationCanceledException(cancellationToken);
			} catch (OperationCanceledException) when (grace.IsCancellationRequested) {
				// The request write and exit wait share the same deadline.
			} catch (Exception error) when ((error is IOException or ObjectDisposedException) && backend.Exit.IsCompletedSuccessfully) {
				return Collected(backend, false);
			}
		}
		cancellationToken.ThrowIfCancellationRequested();
		if (backend.Exit.IsCompletedSuccessfully) return Collected(backend, false);
		if (!forceTermination) return new(PtyShutdownStatus.TimedOut, null, false);
		cancellationToken.ThrowIfCancellationRequested();
		PtyControlResult control = backend.RequestTermination(target);
		try {
			int code = await backend.Exit.WaitAsync(terminationTimeout, cancellationToken).ConfigureAwait(false);
			return new(PtyShutdownStatus.Exited, code, true) { TerminationResult = control };
		} catch (TimeoutException) {
			cancellationToken.ThrowIfCancellationRequested();
			return (backend.Exit.IsCompletedSuccessfully ? Collected(backend, true) : new(PtyShutdownStatus.TimedOut, null, true)) with { TerminationResult = control };
		}
	}
	private static PtyShutdownResult Collected(IPtyBackend backend, bool forced) => new(PtyShutdownStatus.Exited, backend.Exit.Result, forced);
}
