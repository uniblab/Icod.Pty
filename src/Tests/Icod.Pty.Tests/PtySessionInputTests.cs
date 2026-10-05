using Icod.Pty.Session;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtySessionInputTests {
	[Fact]
	public async Task Admitted_writes_never_overlap_and_etx_is_ordered() {
		using GateWriteStream stream = new(); SessionWriter writer = new(stream);
		Task first = writer.WriteAsync("ABC"u8.ToArray(), default).AsTask(); await stream.Entered.Task;
		Task second = writer.WriteAsync("DEF"u8.ToArray(), default).AsTask(); Assert.False(second.IsCompleted);
		stream.Release.SetResult(); await Task.WhenAll(first, second);
		await writer.WriteAsync(new byte[] { 3 }, default);
		Assert.Equal("ABCDEF\x03"u8.ToArray(), stream.ToArray()); Assert.Equal(1, stream.MaximumActive);
	}
	[Fact]
	public async Task Cancel_before_admission_writes_nothing() {
		using GateWriteStream stream = new(); SessionWriter writer = new(stream);
		Task first = writer.WriteAsync("ABC"u8.ToArray(), default).AsTask(); await stream.Entered.Task;
		using CancellationTokenSource cancel = new();
		Task queued = writer.WriteAsync("DEF"u8.ToArray(), cancel.Token).AsTask(); cancel.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
		stream.Release.SetResult(); await first; Assert.Equal("ABC"u8.ToArray(), stream.ToArray());
	}
	[Fact]
	public async Task Cancel_after_prefix_does_not_retry_bytes() {
		using GateWriteStream stream = new() { Prefix = 2 }; SessionWriter writer = new(stream);
		using CancellationTokenSource cancel = new();
		Task write = writer.WriteAsync("ABCDEF"u8.ToArray(), cancel.Token).AsTask(); await stream.Entered.Task; cancel.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
		Assert.Equal("AB"u8.ToArray(), stream.ToArray()); Assert.Equal(1, stream.Calls);
	}
	[Fact]
	public async Task Seal_cancels_normal_writes_but_private_shutdown_is_allowed() {
		using GateWriteStream stream = new(); SessionWriter writer = new(stream);
		Task first = writer.WriteAsync("ABC"u8.ToArray(), default).AsTask(); await stream.Entered.Task;
		Task second = writer.WriteAsync("DEF"u8.ToArray(), default).AsTask(); writer.Seal();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
		await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.WriteAsync(new byte[] { 3 }, default));
		await writer.WriteShutdownAsync("quit"u8.ToArray(), default);
		Assert.Equal("quit"u8.ToArray(), stream.ToArray());
	}
	[Fact]
	public async Task Stop_cancels_private_write_and_rejects_both_paths() {
		using GateWriteStream stream = new(); SessionWriter writer = new(stream);
		Task write = writer.WriteShutdownAsync("quit"u8.ToArray(), default).AsTask(); await stream.Entered.Task; writer.Stop();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
		await Assert.ThrowsAsync<ObjectDisposedException>(async () => await writer.WriteAsync(new byte[] { 3 }, default));
		await Assert.ThrowsAsync<ObjectDisposedException>(async () => await writer.WriteShutdownAsync(new byte[] { 3 }, default));
	}
}
