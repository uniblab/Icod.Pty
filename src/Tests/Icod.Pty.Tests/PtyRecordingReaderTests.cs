using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyRecordingReaderTests {
	private static readonly byte[] Golden = [
		0x49, 0x63, 0x6f, 0x64, 0x50, 0x74, 0x79, 0x00, 0x01, 0x00, 0x10, 0x00, 0x50, 0x00, 0x18, 0x00,
		0x01, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0xff, 0x41,
		0x02, 0x00, 0x00, 0x00, 0x0a, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x64, 0x00, 0x28, 0x00,
		0x03, 0x00, 0x00, 0x00, 0x0a, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
	];

	[Fact]
	public async Task Hand_authored_golden_file_decodes_portably() {
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(Golden));
		Assert.Equal(new PtySize(80, 24), reader.InitialSize);
		PtyRecordingEvent output = Assert.IsType<PtyRecordingEvent>(await reader.ReadAsync());
		Assert.Equal(PtyRecordingEventKind.Output, output.Kind);
		Assert.Equal(TimeSpan.FromTicks(5), output.Elapsed);
		Assert.Equal(new byte[] { 0, 255, 65 }, output.Output.ToArray());
		PtyRecordingEvent resize = Assert.IsType<PtyRecordingEvent>(await reader.ReadAsync());
		Assert.Equal(PtyRecordingEventKind.Resize, resize.Kind);
		Assert.Equal(new PtySize(100, 40), resize.Size);
		Assert.Null(await reader.ReadAsync());
		Assert.Equal(PtyRecordingStatus.Complete, reader.FinalStatus);
	}

	[Fact]
	public async Task Replay_copies_only_output_and_reports_all_events() {
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(Golden));
		using MemoryStream destination = new();
		PtyRecordingReplayResult result = await reader.ReplayAsync(destination);
		Assert.Equal(new byte[] { 0, 255, 65 }, destination.ToArray());
		Assert.Equal(PtyRecordingStatus.Complete, result.Status);
		Assert.Equal(3, result.OutputBytes);
		Assert.Equal(2, result.EventCount);
	}

	[Fact]
	public async Task Valid_truncated_prefix_is_distinct_from_damage() {
		byte[] bytes = Golden.ToArray(); bytes[55] = 0x04;
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(bytes));
		while (await reader.ReadAsync() != null) { }
		Assert.Equal(PtyRecordingStatus.Truncated, reader.FinalStatus);
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(8, 2)]
	[InlineData(16, 9)]
	[InlineData(39, 4)]
	[InlineData(28, 255)]
	[InlineData(35, 9)]
	[InlineData(51, 0)]
	[InlineData(55, 9)]
	public async Task Invalid_structure_is_rejected_without_payload_text(int index, int value) {
		byte[] bytes = Golden.ToArray(); bytes[index] = (byte)value;
		PtyRecordingFormatException error = await Assert.ThrowsAsync<PtyRecordingFormatException>(async () => {
			await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(bytes));
			while (await reader.ReadAsync() != null) { }
		});
		Assert.DoesNotContain("ÿA", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Missing_terminal_and_trailing_data_are_rejected() {
		await Assert.ThrowsAsync<PtyRecordingFormatException>(async () => {
			await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(Golden[..^16]));
			while (await reader.ReadAsync() != null) { }
		});
		await Assert.ThrowsAsync<PtyRecordingFormatException>(async () => {
			await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream([.. Golden, 1]));
			while (await reader.ReadAsync() != null) { }
		});
	}

	[Fact]
	public async Task Reader_limits_are_checked_before_payload_allocation() {
		byte[] bytes = Golden.ToArray();
		bytes[28] = 1; bytes[29] = 0; bytes[30] = 1; bytes[31] = 0;
		await Assert.ThrowsAsync<PtyRecordingFormatException>(async () => {
			await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(bytes),
				new PtyRecordingReaderOptions { MaxFrameBytes = 16384 });
			await reader.ReadAsync();
		});
	}

	[Fact]
	public async Task Reader_respects_source_ownership() {
		MemoryStream source = new(Golden); await using (PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(source,
			new PtyRecordingReaderOptions { LeaveOpen = false })) { while (await reader.ReadAsync() != null) { } }
		Assert.False(source.CanRead);
	}

	[Fact]
	public async Task Replay_rejects_the_recording_source_as_destination() {
		using MemoryStream source = new(Golden); await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(source);
		await Assert.ThrowsAsync<ArgumentException>(() => reader.ReplayAsync(source));
	}
}
