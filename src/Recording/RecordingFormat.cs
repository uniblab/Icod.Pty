using System.Buffers.Binary;

namespace Icod.Pty.Recording;

internal static class RecordingFormat {
	internal const int HeaderSize = 16, FrameHeaderSize = 16, TerminalSize = 16, MaxOutputFrame = 16 * 1024;
	internal const byte Output = 1, Resize = 2, Complete = 3, Truncated = 4, Stopped = 5;
	internal static ReadOnlySpan<byte> Magic => "IcodPty\0"u8;
	internal static byte[] Header(PtySize size) {
		byte[] bytes = new byte[HeaderSize]; Magic.CopyTo(bytes); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 1);
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), HeaderSize);
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), checked((ushort)size.Columns));
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), checked((ushort)size.Rows)); return bytes;
	}
	internal static byte[] Frame(byte kind, long ticks, int payloadLength) {
		byte[] bytes = new byte[FrameHeaderSize]; bytes[0] = kind;
		BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(4), ticks);
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), checked((uint)payloadLength)); return bytes;
	}
}
