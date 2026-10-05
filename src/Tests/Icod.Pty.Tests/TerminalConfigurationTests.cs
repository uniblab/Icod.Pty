using System.Runtime.InteropServices;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class TerminalConfigurationTests {
	private const PtyTerminalCapabilities All = PtyTerminalCapabilities.RawProfile | PtyTerminalCapabilities.Echo | PtyTerminalCapabilities.CanonicalInput | PtyTerminalCapabilities.SignalProcessing | PtyTerminalCapabilities.ControlCharacters | PtyTerminalCapabilities.ReadTiming;

	[Fact]
	public void Default_options_are_noop() {
		Assert.Equal(-1, PtyTerminalOptions.DisabledCharacter);
		Assert.Equal(0, (int)PtyTerminalProfile.Preserve);
		Assert.Equal(1, (int)PtyTerminalProfile.Raw);
		Assert.Equal(0, (int)PtyTerminalCapabilities.None);
		Assert.Equal(1, (int)PtyTerminalCapabilities.RawProfile);
		Assert.Equal(2, (int)PtyTerminalCapabilities.Echo);
		Assert.Equal(4, (int)PtyTerminalCapabilities.CanonicalInput);
		Assert.Equal(8, (int)PtyTerminalCapabilities.SignalProcessing);
		Assert.Equal(16, (int)PtyTerminalCapabilities.ControlCharacters);
		Assert.Equal(32, (int)PtyTerminalCapabilities.ReadTiming);
		Assert.Null(TerminalConfiguration.Capture(null, PtyTerminalCapabilities.None));
		Assert.Null(TerminalConfiguration.Capture(new(), PtyTerminalCapabilities.None));
		Assert.Null(new PtyStartInfo("unused").TerminalOptions);
	}

	[Fact]
	public void Capture_is_detached() {
		PtyTerminalOptions options = new() {
			Echo = false, CanonicalInput = false, SignalProcessing = true,
			InterruptCharacter = 28, EndOfFileCharacter = 5, EraseCharacter = PtyTerminalOptions.DisabledCharacter,
			MinimumReadBytes = 2, ReadTimeoutDeciseconds = 3
		};
		TerminalConfiguration captured = Assert.IsType<TerminalConfiguration>(TerminalConfiguration.Capture(options, All));
		options.Echo = true; options.CanonicalInput = true; options.SignalProcessing = false;
		options.InterruptCharacter = 1; options.EndOfFileCharacter = 2; options.EraseCharacter = 3;
		options.MinimumReadBytes = null; options.ReadTimeoutDeciseconds = null;
		Assert.Equal(PtyTerminalProfile.Preserve, captured.Profile);
		Assert.Equal(false, captured.Echo); Assert.Equal(false, captured.CanonicalInput); Assert.Equal(true, captured.SignalProcessing);
		Assert.Equal(28, captured.InterruptCharacter); Assert.Equal(5, captured.EndOfFileCharacter); Assert.Equal(PtyTerminalOptions.DisabledCharacter, captured.EraseCharacter);
		Assert.Equal(2, captured.MinimumReadBytes); Assert.Equal(3, captured.ReadTimeoutDeciseconds);
		Assert.Equal(All & ~PtyTerminalCapabilities.RawProfile, captured.RequiredCapabilities);
	}

	[Fact]
	public async Task Invalid_settings_never_reach_factory() {
		PtyTerminalOptions[] invalid = [
			new() { Profile = (PtyTerminalProfile)2 },
			new() { InterruptCharacter = -2 }, new() { InterruptCharacter = 256 },
			new() { EndOfFileCharacter = -2 }, new() { EndOfFileCharacter = 256 },
			new() { EraseCharacter = -2 }, new() { EraseCharacter = 256 },
			new() { MinimumReadBytes = -1, CanonicalInput = false }, new() { MinimumReadBytes = 256, CanonicalInput = false },
			new() { ReadTimeoutDeciseconds = -1, CanonicalInput = false }, new() { ReadTimeoutDeciseconds = 256, CanonicalInput = false },
			new() { MinimumReadBytes = 1 }, new() { ReadTimeoutDeciseconds = 1 },
			new() { MinimumReadBytes = 1, CanonicalInput = true }, new() { ReadTimeoutDeciseconds = 1, CanonicalInput = true }
		];
		foreach (Action<PtyTerminalOptions> setOverride in RawOverrides()) { PtyTerminalOptions raw = new() { Profile = PtyTerminalProfile.Raw }; setOverride(raw); invalid = [.. invalid, raw]; }
		foreach (PtyTerminalOptions options in invalid) {
			int factoryCalls = 0; PtyStartInfo start = PtyTestSupport.Child("exit"); start.TerminalOptions = options;
			Assert.ThrowsAny<ArgumentException>(() => {
				LaunchConfiguration launch = LaunchConfiguration.Capture(start);
				_ = PtyProcess.StartCoreAsync(launch, default, (_, _) => { factoryCalls++; return Task.FromResult<IPtyBackend>(new ControlledBackend()); });
			});
			Assert.Equal(0, factoryCalls);
		}
		foreach (int value in new[] { PtyTerminalOptions.DisabledCharacter, 0, 255 }) {
			Assert.NotNull(TerminalConfiguration.Capture(new() { InterruptCharacter = value }, All));
			Assert.NotNull(TerminalConfiguration.Capture(new() { EndOfFileCharacter = value }, All));
			Assert.NotNull(TerminalConfiguration.Capture(new() { EraseCharacter = value }, All));
		}
		foreach (int value in new[] { 0, 255 }) {
			Assert.NotNull(TerminalConfiguration.Capture(new() { CanonicalInput = false, MinimumReadBytes = value }, All));
			Assert.NotNull(TerminalConfiguration.Capture(new() { CanonicalInput = false, ReadTimeoutDeciseconds = value }, All));
		}
		await Assert.ThrowsAsync<PlatformNotSupportedException>(() => RejectBeforeFactory(new() { Echo = false }));
	}

	[Fact]
	public void Capabilities_are_side_effect_free() {
		PtyTerminalCapabilities expected = (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64 ? All : PtyTerminalCapabilities.None;
		PtyTerminalCapabilities first = PtyProcess.GetTerminalCapabilities();
		Assert.Equal(expected, first);
		for (int i = 0; i < 10; i++) Assert.Equal(first, PtyProcess.GetTerminalCapabilities());
		Assert.Equal(PtyTerminalCapabilities.RawProfile, TerminalConfiguration.Capture(new() { Profile = PtyTerminalProfile.Raw }, All)!.RequiredCapabilities);
		Assert.Throws<PlatformNotSupportedException>(() => TerminalConfiguration.Capture(new() { Echo = false }, PtyTerminalCapabilities.None));
		Assert.Throws<PlatformNotSupportedException>(() => TerminalConfiguration.Capture(new() { CanonicalInput = false }, PtyTerminalCapabilities.None));
		Assert.Throws<PlatformNotSupportedException>(() => TerminalConfiguration.Capture(new() { SignalProcessing = false }, PtyTerminalCapabilities.None));
		Assert.Throws<PlatformNotSupportedException>(() => TerminalConfiguration.Capture(new() { InterruptCharacter = 3 }, PtyTerminalCapabilities.None));
		Assert.Throws<PlatformNotSupportedException>(() => TerminalConfiguration.Capture(new() { CanonicalInput = false, MinimumReadBytes = 1 }, PtyTerminalCapabilities.None));
	}

	[Theory]
	[MemberData(nameof(ExplicitOptionFamilies))]
	public void Windows_explicit_options_fail_before_launch(PtyTerminalOptions options) {
		if (!OperatingSystem.IsWindows()) return;
		PtyStartInfo start = PtyTestSupport.Child("exit"); start.TerminalOptions = options;
		Assert.Throws<PlatformNotSupportedException>(() => { _ = PtyProcess.StartAsync(start); });
		using MemoryStream output = new();
		Assert.Throws<PlatformNotSupportedException>(() => { _ = PtySession.StartAsync(start, new(output)); });
		Assert.True(output.CanWrite);
	}

	[Theory]
	[InlineData(PtyProcessOwnership.PrimaryProcess)]
	[InlineData(PtyProcessOwnership.PlatformScope)]
	public async Task Windows_default_options_preserve_launch(PtyProcessOwnership ownership) {
		if (!OperatingSystem.IsWindows()) return;
		PtyStartInfo start = PtyTestSupport.Child("exit"); start.Ownership = ownership; start.TerminalOptions = new();
		await using PtyProcess process = await PtyProcess.StartAsync(start);
		Assert.Equal(37, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)));
	}

	[Fact]
	public void Rejected_options_do_not_change_host_console() {
		if (!OperatingSystem.IsWindows()) return;
		ConsoleSnapshot before = ConsoleSnapshot.Capture();
		PtyStartInfo start = PtyTestSupport.Child("exit"); start.TerminalOptions = new() { Echo = false };
		Assert.Throws<PlatformNotSupportedException>(() => PtyProcess.Start(start));
		Assert.Equal(before, ConsoleSnapshot.Capture());
	}

	public static IEnumerable<object[]> ExplicitOptionFamilies() {
		yield return [new PtyTerminalOptions { Profile = PtyTerminalProfile.Raw }];
		yield return [new PtyTerminalOptions { Echo = false }];
		yield return [new PtyTerminalOptions { CanonicalInput = false }];
		yield return [new PtyTerminalOptions { SignalProcessing = false }];
		yield return [new PtyTerminalOptions { InterruptCharacter = 3 }];
		yield return [new PtyTerminalOptions { CanonicalInput = false, MinimumReadBytes = 1 }];
	}

	private static IEnumerable<Action<PtyTerminalOptions>> RawOverrides() {
		yield return value => value.Echo = false;
		yield return value => value.CanonicalInput = false;
		yield return value => value.SignalProcessing = false;
		yield return value => value.InterruptCharacter = 3;
		yield return value => value.EndOfFileCharacter = 4;
		yield return value => value.EraseCharacter = 8;
		yield return value => value.MinimumReadBytes = 1;
		yield return value => value.ReadTimeoutDeciseconds = 0;
	}

	private static async Task RejectBeforeFactory(PtyTerminalOptions options) {
		int calls = 0;
		try {
			TerminalConfiguration? configuration = TerminalConfiguration.Capture(options, PtyTerminalCapabilities.None);
			if (configuration != null) { calls++; await Task.Yield(); }
		} finally { Assert.Equal(0, calls); }
	}

	private readonly record struct ConsoleSnapshot(uint InputMode, uint OutputMode, uint InputCodePage, uint OutputCodePage) {
		internal static ConsoleSnapshot Capture() {
			nint input = GetStdHandle(-10), output = GetStdHandle(-11);
			GetConsoleMode(input, out uint inputMode); GetConsoleMode(output, out uint outputMode);
			return new(inputMode, outputMode, GetConsoleCP(), GetConsoleOutputCP());
		}
		[DllImport("kernel32.dll")] private static extern nint GetStdHandle(int handle);
		[DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetConsoleMode(nint handle, out uint mode);
		[DllImport("kernel32.dll")] private static extern uint GetConsoleCP();
		[DllImport("kernel32.dll")] private static extern uint GetConsoleOutputCP();
	}
}
