using System.Reflection;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyTimedPlaybackOptionsTests {
	[Fact]
	public void Default_cap_is_one_hour() {
		PtyRecordingTimedPlaybackOptions options = new();
		Assert.Equal(TimeSpan.FromHours(1), options.MaxEventElapsed);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task Zero_or_negative_cap_is_rejected_before_read(long ticks) {
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(HeaderOnly));
		PtyRecordingTimedPlaybackOptions options = new() { MaxEventElapsed = TimeSpan.FromTicks(ticks) };
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reader.PlayTimedAsync((_, _) => ValueTask.CompletedTask, options));
	}

	[Fact]
	public async Task Null_callback_is_rejected_before_read() {
		await using PtyRecordingReader reader = await PtyRecordingReader.OpenAsync(new MemoryStream(HeaderOnly));
		await Assert.ThrowsAsync<ArgumentNullException>(() => reader.PlayTimedAsync(null!));
	}

	[Fact]
	public void Immediate_replay_remains_available_with_unchanged_signature() {
		MethodInfo method = Assert.Single(typeof(PtyRecordingReader).GetMethods(), method => method.Name == nameof(PtyRecordingReader.ReplayAsync));
		Assert.Equal(typeof(Task<PtyRecordingReplayResult>), method.ReturnType);
		ParameterInfo[] parameters = method.GetParameters();
		Assert.Collection(parameters,
			parameter => { Assert.Equal("destination", parameter.Name); Assert.Equal(typeof(Stream), parameter.ParameterType); Assert.False(parameter.HasDefaultValue); },
			parameter => { Assert.Equal("cancellationToken", parameter.Name); Assert.Equal(typeof(CancellationToken), parameter.ParameterType); Assert.True(parameter.HasDefaultValue); Assert.Null(parameter.DefaultValue); });
	}

	[Fact]
	public void Timed_playback_signature_is_exact() {
		MethodInfo method = Assert.Single(typeof(PtyRecordingReader).GetMethods(), method => method.Name == nameof(PtyRecordingReader.PlayTimedAsync));
		Assert.Equal(typeof(Task<PtyRecordingReplayResult>), method.ReturnType);
		ParameterInfo[] parameters = method.GetParameters();
		Assert.Collection(parameters,
			parameter => { Assert.Equal("onEvent", parameter.Name); Assert.Equal(typeof(Func<PtyRecordingEvent, CancellationToken, ValueTask>), parameter.ParameterType); Assert.False(parameter.HasDefaultValue); },
			parameter => { Assert.Equal("options", parameter.Name); Assert.Equal(typeof(PtyRecordingTimedPlaybackOptions), parameter.ParameterType); Assert.True(parameter.HasDefaultValue); Assert.Null(parameter.DefaultValue); },
			parameter => { Assert.Equal("cancellationToken", parameter.Name); Assert.Equal(typeof(CancellationToken), parameter.ParameterType); Assert.True(parameter.HasDefaultValue); Assert.Null(parameter.DefaultValue); });
	}

	private static readonly byte[] HeaderOnly = [
		0x49, 0x63, 0x6f, 0x64, 0x50, 0x74, 0x79, 0x00, 0x01, 0x00, 0x10, 0x00, 0x50, 0x00, 0x18, 0x00
	];
}
