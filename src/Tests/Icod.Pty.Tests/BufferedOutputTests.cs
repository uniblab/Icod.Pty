using Icod.Pty.Unix;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class BufferedOutputTests {
	[Fact]
	public async Task Prefetch_is_bounded_and_cancellation_keeps_buffered_bytes() {
		CountingStream source = new(); using BufferedPtyOutputStream output = new(source);
		await source.Backpressured.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Task.Delay(100);
		Assert.InRange(source.Reads, 16, 17);
		using CancellationTokenSource cancel = new(); cancel.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await output.ReadAsync(new byte[32], cancel.Token));
		byte[] bytes = new byte[4096]; Assert.Equal(bytes.Length, await output.ReadAsync(bytes));
		Assert.All(bytes, value => Assert.Equal(42, value));
	}
	[Fact]
	public async Task Prefetch_preserves_all_final_bytes_and_eof() {
		byte[] bytes = Enumerable.Range(0, 100003).Select(i => (byte)i).ToArray();
		using BufferedPtyOutputStream output = new(new MemoryStream(bytes));
		using MemoryStream result = new(); await output.CopyToAsync(result);
		Assert.Equal(bytes, result.ToArray()); Assert.Equal(0, await output.ReadAsync(new byte[1]));
	}
	[Fact]
	public async Task Prefetch_disposal_unblocks_a_pending_reader() {
		using BufferedPtyOutputStream output = new(new BlockingStream());
		Task<int> read = output.ReadAsync(new byte[1]).AsTask(); output.Dispose();
		try { Assert.Equal(0, await read.WaitAsync(TimeSpan.FromSeconds(5))); }
		catch (Exception error) when (error is ObjectDisposedException or OperationCanceledException) { }
	}
	private sealed class CountingStream : MemoryStream {
		internal int Reads;
		internal TaskCompletionSource Backpressured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
			cancellationToken.ThrowIfCancellationRequested(); buffer.Span.Fill(42);
			if (Interlocked.Increment(ref Reads) == 17) Backpressured.TrySetResult(); return ValueTask.FromResult(buffer.Length);
		}
	}
	private sealed class BlockingStream : MemoryStream {
		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
	}
}
