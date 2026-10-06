using System.Buffers.Binary;
using Icod.Pty.Recording;

namespace Icod.Pty;

/// <summary>Validates and streams a portable Icod.Pty recording without launching a process.</summary>
public sealed class PtyRecordingReader : IDisposable, IAsyncDisposable {
	private readonly Stream source;
	private readonly bool leaveOpen;
	private readonly long maxBytes;
	private readonly int maxFrameBytes;
	private long consumed = RecordingFormat.HeaderSize, previousTicks;
	private bool reading, ended, disposed;
	private PtyRecordingReader(Stream source, bool leaveOpen, long maxBytes, int maxFrameBytes, PtySize initialSize) {
		this.source = source; this.leaveOpen = leaveOpen; this.maxBytes = maxBytes; this.maxFrameBytes = maxFrameBytes; InitialSize = initialSize;
	}
	/// <summary>Gets the initial terminal size stored in the header.</summary>
	public PtySize InitialSize { get; }
	/// <summary>Gets the terminal status after the complete file has been consumed.</summary>
	public PtyRecordingStatus? FinalStatus { get; private set; }
	/// <summary>Opens and validates a recording header.</summary>
	public static async Task<PtyRecordingReader> OpenAsync(Stream source, PtyRecordingReaderOptions? options = null,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(source); if (!source.CanRead) throw new ArgumentException("The recording source must be readable.", nameof(source));
		options ??= new(); long maxBytes = options.MaxBytes; int maxFrame = options.MaxFrameBytes;
		if (maxBytes < PtyRecordingOptions.MinimumBytes) throw new ArgumentOutOfRangeException(nameof(options), $"MaxBytes must be at least {PtyRecordingOptions.MinimumBytes}.");
		if (maxFrame < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxFrameBytes must be positive.");
		byte[] header = new byte[RecordingFormat.HeaderSize];
		await ReadExactAsync(source, header, cancellationToken, "Recording header is incomplete.").ConfigureAwait(false);
		if (!header.AsSpan(0, 8).SequenceEqual(RecordingFormat.Magic)) throw Format("Recording magic is invalid.");
		if (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8)) != 1) throw Format("Recording version is unsupported.");
		if (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10)) != RecordingFormat.HeaderSize) throw Format("Recording header size is invalid.");
		try {
			PtySize size = new(BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12)), BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14)));
			return new(source, options.LeaveOpen, maxBytes, maxFrame, size);
		} catch (ArgumentOutOfRangeException) { throw Format("Recording initial size is invalid."); }
	}
	/// <summary>Reads the next owned event, or null after a validated terminal marker.</summary>
	public async ValueTask<PtyRecordingEvent?> ReadAsync(CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(disposed, this); if (ended) return null;
		if (reading) throw new InvalidOperationException("A recording read is already in progress."); reading = true;
		try {
			byte[] header = new byte[RecordingFormat.FrameHeaderSize];
			await ReadExactAsync(source, header, cancellationToken, "Recording ended without a terminal marker.").ConfigureAwait(false);
			if (header[1] != 0 || header[2] != 0 || header[3] != 0) throw Format("Recording frame flags are invalid.");
			long ticks = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(4));
			if (ticks < 0 || ticks < previousTicks) throw Format("Recording timestamps are invalid."); previousTicks = ticks;
			uint rawLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
			if (rawLength > int.MaxValue) throw Format("Recording frame length exceeds the reader limit."); int length = (int)rawLength;
			if (consumed > maxBytes - RecordingFormat.FrameHeaderSize || length > maxBytes - consumed - RecordingFormat.FrameHeaderSize)
				throw Format("Recording exceeds the reader byte limit."); consumed += RecordingFormat.FrameHeaderSize;
			switch (header[0]) {
				case RecordingFormat.Output:
					if (length > maxFrameBytes) throw Format("Recording output frame exceeds the reader limit.");
					byte[] payload = new byte[length]; await ReadExactAsync(source, payload, cancellationToken, "Recording output frame is incomplete.").ConfigureAwait(false);
					consumed += length; return new(PtyRecordingEventKind.Output, TimeSpan.FromTicks(ticks), payload, null);
				case RecordingFormat.Resize:
					if (length != 4) throw Format("Recording resize frame length is invalid.");
					byte[] sizeBytes = new byte[4]; await ReadExactAsync(source, sizeBytes, cancellationToken, "Recording resize frame is incomplete.").ConfigureAwait(false); consumed += 4;
					try { return new(PtyRecordingEventKind.Resize, TimeSpan.FromTicks(ticks), [],
						new PtySize(BinaryPrimitives.ReadUInt16LittleEndian(sizeBytes), BinaryPrimitives.ReadUInt16LittleEndian(sizeBytes.AsSpan(2)))); }
					catch (ArgumentOutOfRangeException) { throw Format("Recording resize value is invalid."); }
				case RecordingFormat.Complete: return await FinishAsync(PtyRecordingStatus.Complete, length, cancellationToken).ConfigureAwait(false);
				case RecordingFormat.Truncated: return await FinishAsync(PtyRecordingStatus.Truncated, length, cancellationToken).ConfigureAwait(false);
				case RecordingFormat.Stopped: return await FinishAsync(PtyRecordingStatus.Stopped, length, cancellationToken).ConfigureAwait(false);
				default: throw Format("Recording frame kind is unsupported.");
			}
		} finally { reading = false; }
	}
	/// <summary>Copies output event bytes in recorded order and returns the validated terminal outcome.</summary>
	public async Task<PtyRecordingReplayResult> ReplayAsync(Stream destination, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(destination); if (!destination.CanWrite) throw new ArgumentException("The replay destination must be writable.", nameof(destination));
		long bytes = 0, events = 0; PtyRecordingEvent? item;
		while ((item = await ReadAsync(cancellationToken).ConfigureAwait(false)) != null) {
			events++; if (item.Kind == PtyRecordingEventKind.Output) { await destination.WriteAsync(item.Output, cancellationToken).ConfigureAwait(false); bytes += item.Output.Length; }
		}
		await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
		return new(FinalStatus!.Value, bytes, events);
	}
	private async ValueTask<PtyRecordingEvent?> FinishAsync(PtyRecordingStatus status, int length, CancellationToken token) {
		if (length != 0) throw Format("Recording terminal frame length is invalid.");
		byte[] extra = new byte[1]; if (await source.ReadAsync(extra, token).ConfigureAwait(false) != 0) throw Format("Recording has data after its terminal marker.");
		FinalStatus = status; ended = true; return null;
	}
	private static async Task ReadExactAsync(Stream stream, Memory<byte> destination, CancellationToken token, string message) {
		int offset = 0; while (offset < destination.Length) { int count = await stream.ReadAsync(destination[offset..], token).ConfigureAwait(false); if (count == 0) throw Format(message); offset += count; }
	}
	private static PtyRecordingFormatException Format(string message) => new(message);
	/// <summary>Releases the source according to reader ownership.</summary>
	public void Dispose() { if (disposed) return; disposed = true; if (!leaveOpen) source.Dispose(); }
	/// <summary>Releases the source asynchronously according to reader ownership.</summary>
	public async ValueTask DisposeAsync() { if (disposed) return; disposed = true; if (!leaveOpen) await source.DisposeAsync().ConfigureAwait(false); }
}
