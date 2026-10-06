using Icod.Pty.Session;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyAutomationContractTests {
	[Fact]
	public void Automation_options_have_bounded_defaults() {
		PtyAutomationOptions options = new();
		Assert.Equal(64 * 1024, options.MaxBufferedOutputBytes);
		Assert.Equal(1, PtyAutomationOptions.MinimumBufferedOutputBytes);
		Assert.Equal(1024 * 1024, PtyAutomationOptions.MaximumBufferedOutputBytes);
	}

	[Fact]
	public void Session_configuration_captures_automation_options() {
		using MemoryStream output = new();
		PtyAutomationOptions automation = new() { MaxBufferedOutputBytes = 4096 };
		SessionConfiguration captured = SessionConfiguration.Capture(new(output) { Automation = automation });

		automation.MaxBufferedOutputBytes = 8192;

		Assert.Equal(4096, captured.Automation!.MaxBufferedOutputBytes);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1048577)]
	public void Invalid_automation_limits_are_rejected_before_launch(int maximum) {
		using MemoryStream output = new();
		Assert.Throws<ArgumentOutOfRangeException>(() => SessionConfiguration.Capture(new(output) {
			Automation = new() { MaxBufferedOutputBytes = maximum }
		}));
	}

	[Fact]
	public void Automation_rejects_a_competing_input_source() {
		using MemoryStream output = new(), input = new();
		Assert.Throws<ArgumentException>(() => SessionConfiguration.Capture(new(output) {
			Input = input, Automation = new()
		}));
	}

	[Fact]
	public void Script_steps_copy_caller_bytes() {
		byte[] sent = [1, 2, 3], expected = [4, 5, 6];
		PtyScriptStep send = PtyScriptStep.Send(sent);
		PtyScriptStep expect = PtyScriptStep.Expect(expected, TimeSpan.FromSeconds(2));

		sent[0] = 9; expected[0] = 9;

		Assert.Equal(PtyScriptStepKind.Send, send.Kind);
		Assert.Equal(new byte[] { 1, 2, 3 }, send.Bytes.ToArray());
		Assert.Equal(PtyScriptStepKind.Expect, expect.Kind);
		Assert.Equal(new byte[] { 4, 5, 6 }, expect.Bytes.ToArray());
		Assert.Equal(TimeSpan.FromSeconds(2), expect.Timeout);
	}

	[Fact]
	public void Script_expect_rejects_invalid_pattern_and_timeout() {
		Assert.Throws<ArgumentException>(() => PtyScriptStep.Expect(Array.Empty<byte>(), TimeSpan.FromSeconds(1)));
		Assert.Throws<ArgumentOutOfRangeException>(() => PtyScriptStep.Expect(new byte[] { 1 }, TimeSpan.Zero));
		Assert.Throws<ArgumentOutOfRangeException>(() => PtyScriptStep.Expect(new byte[] { 1 }, TimeSpan.FromMilliseconds((double)int.MaxValue + 1)));
	}

	[Fact]
	public async Task Expect_requires_automation_and_valid_arguments() {
		using MemoryStream output = new(); ControlledBackend disabledBackend = new();
		await using (PtySession disabled = await SessionTestSupport.Start(disabledBackend, output)) {
			await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.ExpectAsync(new byte[] { 1 }, TimeSpan.FromSeconds(1)));
			disabledBackend.Completion.TrySetResult(0); await disabled.Completion;
		}

		using MemoryStream enabledOutput = new(); ControlledBackend enabledBackend = new();
		await using (PtySession enabled = await SessionTestSupport.Start(enabledBackend, enabledOutput, automation: new() { MaxBufferedOutputBytes = 2 })) {
			await Assert.ThrowsAsync<ArgumentException>(() => enabled.ExpectAsync(Array.Empty<byte>(), TimeSpan.FromSeconds(1)));
			await Assert.ThrowsAsync<ArgumentException>(() => enabled.ExpectAsync(new byte[] { 1, 2, 3 }, TimeSpan.FromSeconds(1)));
			await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => enabled.ExpectAsync(new byte[] { 1 }, TimeSpan.Zero));
			await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => enabled.ExpectAsync(new byte[] { 1 }, TimeSpan.FromMilliseconds((double)int.MaxValue + 1)));
			enabledBackend.Completion.TrySetResult(0); await enabled.Completion;
		}
	}
}
