using Icod.Pty.Session;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyRecordingContractTests {
	[Fact]
	public void Recording_options_have_bounded_leave_open_defaults() {
		using MemoryStream destination = new();
		PtyRecordingOptions options = new(destination);
		Assert.Same(destination, options.Destination);
		Assert.True(options.LeaveOpen);
		Assert.Equal(16L * 1024 * 1024, options.MaxBytes);
	}

	[Fact]
	public void Session_configuration_captures_recording_options() {
		using MemoryStream output = new(), recording = new(), replacement = new();
		PtyRecordingOptions recordingOptions = new(recording) { LeaveOpen = false, MaxBytes = 4096 };
		PtySessionOptions options = new(output) { Recording = recordingOptions };

		SessionConfiguration captured = SessionConfiguration.Capture(options);
		recordingOptions.Destination = replacement;
		recordingOptions.LeaveOpen = true;
		recordingOptions.MaxBytes = 8192;

		Assert.Same(recording, captured.Recording!.Destination);
		Assert.False(captured.Recording.LeaveOpen);
		Assert.Equal(4096, captured.Recording.MaxBytes);
	}

	[Fact]
	public void Invalid_recording_streams_and_limits_are_rejected_before_launch() {
		using MemoryStream output = new(), input = new(), recording = new();
		Assert.Throws<ArgumentException>(() => SessionConfiguration.Capture(new(output) { Recording = new(output) }));
		Assert.Throws<ArgumentException>(() => SessionConfiguration.Capture(new(output) { Input = input, Recording = new(input) }));
		using MemoryStream readOnly = new([], false);
		Assert.Throws<ArgumentException>(() => SessionConfiguration.Capture(new(output) { Recording = new(readOnly) }));
		PtyRecordingOptions missing = new(recording) { Destination = null! };
		Assert.Throws<ArgumentException>(() => SessionConfiguration.Capture(new(output) { Recording = missing }));
		Assert.Throws<ArgumentOutOfRangeException>(() => SessionConfiguration.Capture(new(output) {
			Recording = new(recording) { MaxBytes = PtyRecordingOptions.MinimumBytes - 1 }
		}));
	}

	[Fact]
	public async Task Failed_start_does_not_take_recording_stream_ownership() {
		using MemoryStream output = new(), recording = new();
		SessionConfiguration configuration = SessionConfiguration.Capture(new(output) {
			Recording = new(recording) { LeaveOpen = false }
		});
		await Assert.ThrowsAsync<IOException>(() => PtySession.StartCoreAsync(ControlledBackend.Launch(), configuration,
			default, (_, _) => Task.FromException<PtyProcess>(new IOException("start"))));
		Assert.True(recording.CanWrite);
	}

	[Fact]
	public void Recording_result_exposes_disabled_contract() {
		PtyRecordingResult result = PtyRecordingResult.Disabled;
		Assert.Equal(PtyRecordingStatus.Disabled, result.Status);
		Assert.Equal(0, result.BytesWritten);
		Assert.Equal(0, result.EventCount);
		Assert.Null(result.Exception);
	}
}
