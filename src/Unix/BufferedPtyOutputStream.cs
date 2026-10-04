using System.Threading.Channels;

namespace Icod.Pty.Unix;

// Darwin drains on slave close and then discards unread data. Read ahead even
// before the caller reads, but bound the queue so large output still backpressures.
internal sealed class BufferedPtyOutputStream : Stream {
	private readonly Stream source;
	private readonly CancellationTokenSource stop = new();
	private readonly Channel<ReadOnlyMemory<byte>> chunks = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(16) {
		SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
	});
	private readonly Task pump;
	private ReadOnlyMemory<byte> pending;
	private int disposed;
	internal BufferedPtyOutputStream(Stream source) { this.source = source; pump = Task.Run(PumpAsync); }
	private async Task PumpAsync() {
		Exception? failure = null;
		try {
			while (true) {
				byte[] bytes = new byte[4096];
				int count = await source.ReadAsync(bytes, stop.Token).ConfigureAwait(false);
				if (count == 0) break;
				await chunks.Writer.WriteAsync(bytes.AsMemory(0, count), stop.Token).ConfigureAwait(false);
			}
		} catch (Exception error) when (stop.IsCancellationRequested && error is OperationCanceledException or ObjectDisposedException) { }
		catch (Exception error) { failure = error; }
		finally { chunks.Writer.TryComplete(failure); }
	}
	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this); cancellationToken.ThrowIfCancellationRequested();
		if (buffer.Length == 0) return 0;
		while (pending.IsEmpty) {
			if (!await chunks.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) return 0;
			if (!chunks.Reader.TryRead(out pending)) continue;
		}
		int count = Math.Min(buffer.Length, pending.Length);
		pending.Span[..count].CopyTo(buffer.Span); pending = pending[count..]; return count;
	}
	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
		ValidateBufferArguments(buffer, offset, count); return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
	}
	public override int Read(byte[] buffer, int offset, int count) { ValidateBufferArguments(buffer, offset, count); return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult(); }
	public override int Read(Span<byte> buffer) { byte[] copy = new byte[buffer.Length]; int count = Read(copy, 0, copy.Length); copy.AsSpan(0, count).CopyTo(buffer); return count; }
	public override bool CanRead => Volatile.Read(ref disposed) == 0;
	public override bool CanWrite => false;
	public override bool CanSeek => false;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override void Flush() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	protected override void Dispose(bool disposing) {
		if (disposing && Interlocked.Exchange(ref disposed, 1) == 0) {
			stop.Cancel();
			try { source.Dispose(); }
			finally { pump.GetAwaiter().GetResult(); stop.Dispose(); }
		}
		base.Dispose(disposing);
	}
}
