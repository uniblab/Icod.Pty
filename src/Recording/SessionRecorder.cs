using System.Buffers.Binary;
using Icod.Pty.Session;

namespace Icod.Pty.Recording;

internal sealed class SessionRecorder {
	private readonly RecordingConfiguration configuration;
	private readonly SemaphoreSlim gate = new(1, 1);
	private readonly Func<long> elapsedTicks;
	private readonly TaskCompletionSource<PtyRecordingResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private long bytesWritten, eventCount, previousTicks;
	private Exception? failure;
	private bool terminalWritten, finishing;
	internal SessionRecorder(RecordingConfiguration configuration, PtySize initialSize, Func<long>? elapsedTicks = null) {
		this.configuration = configuration; this.elapsedTicks = elapsedTicks ?? (() => System.Diagnostics.Stopwatch.GetElapsedTime(start).Ticks);
		start = System.Diagnostics.Stopwatch.GetTimestamp();
		try { byte[] header = RecordingFormat.Header(initialSize); configuration.Destination.Write(header); bytesWritten = header.Length; }
		catch (Exception error) { failure = error; }
	}
	private readonly long start;
	internal Task<PtyRecordingResult> Completion => completion.Task;
	internal async ValueTask RecordOutputAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) {
		for (int offset = 0; offset < bytes.Length;) {
			int count = Math.Min(RecordingFormat.MaxOutputFrame, bytes.Length - offset);
			await gate.WaitAsync(token).ConfigureAwait(false);
			try {
				if (!CanRecord(RecordingFormat.FrameHeaderSize + count)) { await TruncateAsync(token).ConfigureAwait(false); return; }
				long ticks = NextTicks(); byte[] header = RecordingFormat.Frame(RecordingFormat.Output, ticks, count);
				await configuration.Destination.WriteAsync(header, token).ConfigureAwait(false);
				await configuration.Destination.WriteAsync(bytes.Slice(offset, count), token).ConfigureAwait(false);
				bytesWritten += header.Length + count; eventCount++; offset += count;
			} catch (Exception error) { Fail(error); return; }
			finally { gate.Release(); }
		}
	}
	internal void RecordResize(PtySize size) {
		gate.Wait();
		try {
			if (!CanRecord(RecordingFormat.FrameHeaderSize + 4)) { Truncate(); return; }
			byte[] header = RecordingFormat.Frame(RecordingFormat.Resize, NextTicks(), 4), payload = new byte[4];
			BinaryPrimitives.WriteUInt16LittleEndian(payload, checked((ushort)size.Columns));
			BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), checked((ushort)size.Rows));
			configuration.Destination.Write(header); configuration.Destination.Write(payload);
			bytesWritten += header.Length + payload.Length; eventCount++;
		} catch (Exception error) { Fail(error); }
		finally { gate.Release(); }
	}
	internal Task<PtyRecordingResult> FinishAsync(PtyRecordingStatus status) {
		lock (completion) { if (!finishing) { finishing = true; _ = FinishCoreAsync(status); } return completion.Task; }
	}
	private async Task FinishCoreAsync(PtyRecordingStatus requested) {
		await gate.WaitAsync().ConfigureAwait(false);
		try {
			if (failure == null && !terminalWritten) await WriteTerminalAsync(requested, CancellationToken.None).ConfigureAwait(false);
			if (failure == null) {
				try { await configuration.Destination.FlushAsync().ConfigureAwait(false); }
				catch (Exception error) { Fail(error); }
			}
			if (!configuration.LeaveOpen) {
				try { await configuration.Destination.DisposeAsync().ConfigureAwait(false); }
				catch (Exception error) { failure ??= error; }
			}
			PtyRecordingStatus final = failure != null ? PtyRecordingStatus.Faulted : terminalStatus;
			completion.TrySetResult(new(final, bytesWritten, eventCount, failure));
		} finally { gate.Release(); }
	}
	private PtyRecordingStatus terminalStatus;
	private bool CanRecord(int size) => failure == null && !terminalWritten && size <= configuration.MaxBytes - bytesWritten - RecordingFormat.TerminalSize;
	private long NextTicks() { long value = Math.Max(0, elapsedTicks()); previousTicks = Math.Max(previousTicks, value); return previousTicks; }
	private async ValueTask TruncateAsync(CancellationToken token) { if (!terminalWritten && failure == null) await WriteTerminalAsync(PtyRecordingStatus.Truncated, token).ConfigureAwait(false); }
	private void Truncate() {
		if (terminalWritten || failure != null) return;
		try { WriteTerminal(PtyRecordingStatus.Truncated); } catch (Exception error) { Fail(error); }
	}
	private async ValueTask WriteTerminalAsync(PtyRecordingStatus status, CancellationToken token) {
		byte kind = TerminalKind(status); byte[] header = RecordingFormat.Frame(kind, NextTicks(), 0);
		await configuration.Destination.WriteAsync(header, token).ConfigureAwait(false); bytesWritten += header.Length; terminalWritten = true; terminalStatus = status;
	}
	private void WriteTerminal(PtyRecordingStatus status) {
		byte[] header = RecordingFormat.Frame(TerminalKind(status), NextTicks(), 0); configuration.Destination.Write(header);
		bytesWritten += header.Length; terminalWritten = true; terminalStatus = status;
	}
	private static byte TerminalKind(PtyRecordingStatus status) => status switch {
		PtyRecordingStatus.Complete => RecordingFormat.Complete,
		PtyRecordingStatus.Truncated => RecordingFormat.Truncated,
		PtyRecordingStatus.Stopped => RecordingFormat.Stopped,
		_ => throw new ArgumentOutOfRangeException(nameof(status))
	};
	private void Fail(Exception error) { failure ??= error; }
}
