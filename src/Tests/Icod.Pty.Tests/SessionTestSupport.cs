using Icod.Pty.Session;

namespace Icod.Pty.Tests;

internal static class SessionTestSupport {
	internal static Task<PtySession> Start(ControlledBackend backend, Stream output, Stream? input = null, TimeSpan? drain = null,
		bool leaveOpen = true, PtyRecordingOptions? recording = null, PtyAutomationOptions? automation = null) => PtySession.StartCoreAsync(ControlledBackend.Launch(), SessionConfiguration.Capture(new(output) {
			Input = input, DrainTimeout = drain ?? TimeSpan.FromSeconds(5), LeaveInputOpen = leaveOpen, LeaveOutputOpen = leaveOpen,
			Recording = recording, Automation = automation
		}), default, (_, token) => backend.Start(token));
	internal static async Task Until(Func<bool> condition) {
		using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
		while (!condition()) await Task.Delay(10, deadline.Token);
	}
	internal static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class GateWriteStream : MemoryStream {
	internal readonly TaskCompletionSource Entered = SessionTestSupport.NewSignal(), Release = SessionTestSupport.NewSignal();
	internal int Prefix, Calls, Active, MaximumActive;
	internal bool IgnoreCancellation;
	internal Exception? Failure;
	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) {
		int active = Interlocked.Increment(ref Active); MaximumActive = Math.Max(MaximumActive, active);
		try {
			if (Interlocked.Increment(ref Calls) == 1) {
				if (Prefix != 0) base.Write(buffer.Span[..Prefix]);
				Entered.TrySetResult();
				if (IgnoreCancellation) await Release.Task; else await Release.Task.WaitAsync(token);
				buffer = buffer[Prefix..];
			}
			if (Failure != null) throw Failure; token.ThrowIfCancellationRequested(); base.Write(buffer.Span);
		} finally { Interlocked.Decrement(ref Active); }
	}
}

internal sealed class TrackingStream : MemoryStream {
	internal int Flushes, Disposals;
	internal Exception? FlushFailure;
	public override Task FlushAsync(CancellationToken token) { Flushes++; return FlushFailure == null ? Task.CompletedTask : Task.FromException(FlushFailure); }
	protected override void Dispose(bool disposing) { Disposals++; base.Dispose(disposing); }
}

internal sealed class FeedStream : Stream {
	private readonly Queue<byte[]> queue = new();
	private readonly SemaphoreSlim available = new(0);
	private byte[]? current; private int offset; private bool ended;
	internal void Feed(byte[] bytes) { lock (queue) queue.Enqueue(bytes); available.Release(); }
	internal void End() { ended = true; available.Release(); }
	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) {
		while (current == null) {
			lock (queue) { if (queue.Count != 0) current = queue.Dequeue(); else if (ended) return 0; }
			if (current == null) await available.WaitAsync(token);
		}
		int count = Math.Min(buffer.Length, current.Length - offset); current.AsMemory(offset, count).CopyTo(buffer); offset += count;
		if (offset == current.Length) { current = null; offset = 0; } return count;
	}
	public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
	public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override void Flush() { } public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
internal sealed class GateFlushStream : MemoryStream {
	internal readonly TaskCompletionSource Entered = SessionTestSupport.NewSignal(), Release = SessionTestSupport.NewSignal();
	public override async Task FlushAsync(CancellationToken token) { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
}
internal sealed class GateReadStream : Stream {
	internal readonly TaskCompletionSource Entered = SessionTestSupport.NewSignal(), Release = SessionTestSupport.NewSignal(), Finished = SessionTestSupport.NewSignal();
	internal Exception? Failure { get; set; } internal bool IgnoreCancellation { get; set; }
	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) {
		Entered.TrySetResult();
		try { if (IgnoreCancellation) await Release.Task; else await Release.Task.WaitAsync(token); if (Failure != null) throw Failure; return 0; }
		finally { Finished.TrySetResult(); }
	}
	public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
	public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override void Flush() { } public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
