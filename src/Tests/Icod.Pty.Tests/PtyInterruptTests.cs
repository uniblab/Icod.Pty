using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyInterruptTests {
	[Fact]
	public async Task Interrupt_writes_exactly_one_etx_byte() {
		ControlledBackend backend = new(); using PtyProcess process = await backend.Start();
		await process.SendInterruptAsync(); Assert.Equal(new byte[] { 0x03 }, ((MemoryStream)backend.Input).ToArray());
	}
	[Fact]
	public async Task Raw_child_receives_interrupt_as_input() {
		await using PtyProcess process = await PtyProcess.StartAsync(PtyTestSupport.Child("raw-input"));
		await PtyTestSupport.ReadUntil(process.Output, "RAW-READY");
		await process.SendInterruptAsync();
		await PtyTestSupport.ReadUntil(process.Output, "BYTE:03");
		Assert.False(process.HasExited);
	}
	[Fact]
	public async Task Processed_child_handles_interrupt_and_remains_usable() {
		await using PtyProcess process = await PtyProcess.StartAsync(PtyTestSupport.Child("interrupt-handler"));
		await PtyTestSupport.ReadUntil(process.Output, "INTERRUPT-READY");
		await process.SendInterruptAsync();
		await PtyTestSupport.ReadUntil(process.Output, "INTERRUPT-ACK");
		Assert.False(process.HasExited);
		await process.Input.WriteAsync(PtyTestSupport.Line("quit"));
		Task<string> drain = PtyTestSupport.Drain(process.Output);
		Assert.Equal(23, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)));
		Assert.Contains("BYE-MARKER", await drain);
	}
	[Fact]
	public async Task Interrupt_after_exit_is_rejected() {
		ControlledBackend backend = new(); using PtyProcess process = await backend.Start(); backend.Completion.SetResult(0);
		await Assert.ThrowsAsync<InvalidOperationException>(async () => await process.SendInterruptAsync());
		Assert.Equal(0, backend.Input.Length);
	}
	[Fact]
	public async Task Cancelled_interrupt_does_not_terminate_child() {
		ControlledBackend backend = new(); using PtyProcess process = await backend.Start();
		using CancellationTokenSource cancel = new(); cancel.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await process.SendInterruptAsync(cancel.Token));
		Assert.False(process.HasExited); Assert.Equal(0, backend.TerminateCount); Assert.Equal(0, backend.Input.Length);
	}
	[Fact]
	public async Task Interrupt_after_disposal_is_rejected() {
		ControlledBackend backend = new(); PtyProcess process = await backend.Start(); process.Dispose();
		await Assert.ThrowsAsync<ObjectDisposedException>(async () => await process.SendInterruptAsync());
	}
}
