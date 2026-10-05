namespace Icod.Pty;

internal sealed record TerminalConfiguration(
	PtyTerminalProfile Profile,
	bool? Echo,
	bool? CanonicalInput,
	bool? SignalProcessing,
	int? InterruptCharacter,
	int? EndOfFileCharacter,
	int? EraseCharacter,
	int? MinimumReadBytes,
	int? ReadTimeoutDeciseconds,
	PtyTerminalCapabilities RequiredCapabilities) {

	internal static TerminalConfiguration? Capture(PtyTerminalOptions? options, PtyTerminalCapabilities capabilities) {
		if (options == null) return null;
		PtyTerminalProfile profile = options.Profile;
		bool? echo = options.Echo, canonical = options.CanonicalInput, signals = options.SignalProcessing;
		int? interrupt = options.InterruptCharacter, endOfFile = options.EndOfFileCharacter, erase = options.EraseCharacter;
		int? minimum = options.MinimumReadBytes, timeout = options.ReadTimeoutDeciseconds;

		if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(options), "Profile is not defined.");
		ValidateCharacter(interrupt, nameof(options.InterruptCharacter));
		ValidateCharacter(endOfFile, nameof(options.EndOfFileCharacter));
		ValidateCharacter(erase, nameof(options.EraseCharacter));
		ValidateTiming(minimum, nameof(options.MinimumReadBytes));
		ValidateTiming(timeout, nameof(options.ReadTimeoutDeciseconds));
		bool hasOverride = echo.HasValue || canonical.HasValue || signals.HasValue || interrupt.HasValue || endOfFile.HasValue || erase.HasValue || minimum.HasValue || timeout.HasValue;
		if (profile == PtyTerminalProfile.Raw && hasOverride) throw new ArgumentException("Raw cannot be combined with explicit terminal overrides.", nameof(options));
		if ((minimum.HasValue || timeout.HasValue) && canonical != false) throw new ArgumentException("Read timing requires CanonicalInput=false.", nameof(options));

		PtyTerminalCapabilities required = profile == PtyTerminalProfile.Raw ? PtyTerminalCapabilities.RawProfile : PtyTerminalCapabilities.None;
		if (echo.HasValue) required |= PtyTerminalCapabilities.Echo;
		if (canonical.HasValue) required |= PtyTerminalCapabilities.CanonicalInput;
		if (signals.HasValue) required |= PtyTerminalCapabilities.SignalProcessing;
		if (interrupt.HasValue || endOfFile.HasValue || erase.HasValue) required |= PtyTerminalCapabilities.ControlCharacters;
		if (minimum.HasValue || timeout.HasValue) required |= PtyTerminalCapabilities.ReadTiming;
		if (required == PtyTerminalCapabilities.None) return null;
		PtyTerminalCapabilities unavailable = required & ~capabilities;
		if (unavailable != PtyTerminalCapabilities.None) throw new PlatformNotSupportedException($"Terminal configuration capabilities are unavailable: {unavailable}.");
		return new(profile, echo, canonical, signals, interrupt, endOfFile, erase, minimum, timeout, required);
	}

	private static void ValidateCharacter(int? value, string name) {
		if (value is < PtyTerminalOptions.DisabledCharacter or > byte.MaxValue) throw new ArgumentOutOfRangeException(name, "Control characters must be DisabledCharacter or a byte value.");
	}
	private static void ValidateTiming(int? value, string name) {
		if (value is < byte.MinValue or > byte.MaxValue) throw new ArgumentOutOfRangeException(name, "Read timing values must be between 0 and 255.");
	}
}
