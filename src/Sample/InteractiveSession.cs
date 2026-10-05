namespace Icod.Pty.Sample;

internal static class InteractiveSession {
	internal static async Task<int> RunAsync(PtyStartInfo startInfo, HostConsole console, CancellationToken cancellationToken) {
		if (console.GetSize() is PtySize initial) startInfo.Size = initial;
		using ConsoleInputStream input = new(console);
		PtySession session = await PtySession.StartAsync(startInfo, new(console.Output) { Input = input }, cancellationToken);
		bool disposed = false;
		Exception? pending = null;
		using CancellationTokenSource resizeStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task resize = MonitorSize(session, console, resizeStop.Token);
		try {
			Task completed = await Task.WhenAny(input.Completion, session.Completion, resize).ConfigureAwait(false);
			if (completed == resize) await resize.ConfigureAwait(false);
			if (completed == input.Completion && !session.Completion.IsCompleted && !session.HasExited) {
				try {
					PtyShutdownResult stopped = await session.ShutdownAsync(new() { ForceTermination = true }, cancellationToken).ConfigureAwait(false);
					if (stopped.Status != PtyShutdownStatus.Exited) throw new TimeoutException("The child did not exit within the shutdown deadlines.");
				} catch (ObjectDisposedException) { }
			}
			PtySessionResult result = await session.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
			if (result.OutputStatus == PtySessionOutputStatus.TimedOut) throw new TimeoutException("Output draining timed out; some terminal output may be missing.");
			if (result.Failures.Count != 0) {
				try { await session.DisposeAsync().ConfigureAwait(false); } catch (AggregateException) { }
				disposed = true;
				if (result.Failures.Count == 1) throw result.Failures[0].Exception;
				throw new AggregateException(result.Failures.Select(item => item.Exception));
			}
			return result.ExitCode ?? throw new IOException("The primary process exit status was not collected.");
		} catch (Exception error) {
			pending = error; throw;
		} finally {
			resizeStop.Cancel();
			try { await resize.ConfigureAwait(false); } catch (OperationCanceledException) when (resizeStop.IsCancellationRequested) { }
			if (!disposed) {
				try { await session.DisposeAsync().ConfigureAwait(false); }
				catch (AggregateException cleanup) when (pending != null) {
					if (pending is OperationCanceledException && cleanup.InnerExceptions.Count == 1)
						System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanup.InnerExceptions[0]).Throw();
				}
			}
		}
	}
	internal static async Task ForwardInputAsync(Stream input, HostConsole console, CancellationToken token) {
		byte[] buffer = new byte[4096];
		while (true) {
			int count = await console.ReadAsync(buffer, token).ConfigureAwait(false);
			if (count == 0) return;
			await input.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
		}
	}
	private static async Task MonitorSize(PtySession session, HostConsole console, CancellationToken token) {
		using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
		while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false)) {
			if (console.GetSize() is not PtySize size || size == session.Size) continue;
			try { session.Resize(size); }
			catch (Exception error) when (session.HasExited && error is InvalidOperationException or IOException) { return; }
		}
	}
	private sealed class ConsoleInputStream(HostConsole console) : Stream {
		private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		internal Task Completion => completion.Task;
		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) {
			try { int count = await console.ReadAsync(buffer, token).ConfigureAwait(false); if (count == 0) completion.TrySetResult(); return count; }
			catch (Exception error) { completion.TrySetException(error); throw; }
		}
		public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { } public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
