namespace Icod.Pty.Session;

internal sealed class SessionWriter {
	private readonly Stream input;
	private readonly Action<int>? completed;
	private readonly SemaphoreSlim admission = new(1, 1);
	private readonly object gate = new();
	private readonly CancellationTokenSource normalStop = new(), allStop = new();
	private bool sealedInput, stopped;
	private int pending;
	private TaskCompletionSource idle = CompletedSignal();
	internal SessionWriter(Stream input, Action<int>? completed = null) { this.input = input; this.completed = completed; }
	internal Task Idle { get { lock (gate) return idle.Task; } }
	internal ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) => Write(bytes, token, false);
	internal ValueTask WriteShutdownAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) => Write(bytes, token, true);
	private ValueTask Write(ReadOnlyMemory<byte> bytes, CancellationToken token, bool shutdown) {
		lock (gate) {
			ObjectDisposedException.ThrowIf(stopped, this);
			if (sealedInput && !shutdown) throw new InvalidOperationException("Session input has been sealed.");
			token.ThrowIfCancellationRequested();
			if (pending++ == 0) idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
		}
		return WriteCoreAsync(bytes, token, shutdown);
	}
	private async ValueTask WriteCoreAsync(ReadOnlyMemory<byte> bytes, CancellationToken token, bool shutdown) {
		try {
			using CancellationTokenSource linked = shutdown ? CancellationTokenSource.CreateLinkedTokenSource(token, allStop.Token)
				: CancellationTokenSource.CreateLinkedTokenSource(token, normalStop.Token, allStop.Token);
			await admission.WaitAsync(linked.Token).ConfigureAwait(false);
			try { linked.Token.ThrowIfCancellationRequested(); await input.WriteAsync(bytes, linked.Token).ConfigureAwait(false); completed?.Invoke(bytes.Length); }
			finally { admission.Release(); }
		} finally { lock (gate) { if (--pending == 0) idle.TrySetResult(); } }
	}
	internal void Seal() { lock (gate) sealedInput = true; normalStop.Cancel(); }
	internal void Stop() { lock (gate) stopped = true; allStop.Cancel(); normalStop.Cancel(); }
	private static TaskCompletionSource CompletedSignal() { TaskCompletionSource value = new(TaskCreationOptions.RunContinuationsAsynchronously); value.SetResult(); return value; }
}
