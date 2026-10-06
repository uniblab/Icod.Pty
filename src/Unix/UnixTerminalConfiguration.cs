namespace Icod.Pty.Unix;

internal static class UnixTerminalConfiguration {
	internal static void Apply(int slaveFd, TerminalConfiguration? configuration) => Apply(slaveFd, configuration, UnixTerminalNative.Instance);

	internal static void Apply(int slaveFd, TerminalConfiguration? configuration, IUnixTerminalOperations native) {
		if (configuration == null) return;
		if (native.GetAttributes(slaveFd, out UnixTerminalState original) < 0) throw UnixNative.Error("tcgetattr before terminal configuration", native.LastError);
		UnixTerminalState desired = original.Clone(); UnixTerminalConstants constants = UnixTerminalConstants.For(desired.Platform);
		if (configuration.Profile == PtyTerminalProfile.Raw) {
			native.MakeRaw(desired);
			desired.ControlCharacters[constants.MinimumIndex] = 1;
			desired.ControlCharacters[constants.TimeoutIndex] = 0;
		} else {
			if (configuration.Echo is bool echo) desired.LocalFlags = echo ? desired.LocalFlags | constants.Echo : desired.LocalFlags & ~(constants.Echo | constants.EchoNewline);
			if (configuration.CanonicalInput is bool canonical) desired.LocalFlags = canonical ? desired.LocalFlags | constants.Canonical : desired.LocalFlags & ~constants.Canonical;
			if (configuration.SignalProcessing is bool signals) desired.LocalFlags = signals ? desired.LocalFlags | constants.SignalProcessing : desired.LocalFlags & ~constants.SignalProcessing;
			if (configuration.InterruptCharacter.HasValue || configuration.EndOfFileCharacter.HasValue || configuration.EraseCharacter.HasValue) {
				if (native.GetDisabledCharacter(slaveFd, out byte disabled) < 0) throw UnixNative.Error("fpathconf _PC_VDISABLE", native.LastError);
				SetCharacter(desired, constants.InterruptIndex, configuration.InterruptCharacter, disabled, nameof(PtyTerminalOptions.InterruptCharacter));
				SetCharacter(desired, constants.EndOfFileIndex, configuration.EndOfFileCharacter, disabled, nameof(PtyTerminalOptions.EndOfFileCharacter));
				SetCharacter(desired, constants.EraseIndex, configuration.EraseCharacter, disabled, nameof(PtyTerminalOptions.EraseCharacter));
			}
			if (configuration.MinimumReadBytes is int minimum) desired.ControlCharacters[constants.MinimumIndex] = (byte)minimum;
			if (configuration.ReadTimeoutDeciseconds is int timeout) desired.ControlCharacters[constants.TimeoutIndex] = (byte)timeout;
		}
		if (native.SetAttributes(slaveFd, desired) < 0) throw UnixNative.Error("tcsetattr terminal configuration", native.LastError);
		if (native.GetAttributes(slaveFd, out UnixTerminalState actual) < 0) throw UnixNative.Error("tcgetattr after terminal configuration", native.LastError);
		Verify(configuration, desired, actual, constants);
	}

	private static void SetCharacter(UnixTerminalState state, int index, int? requested, byte disabled, string name) {
		if (!requested.HasValue) return;
		if (requested.Value != PtyTerminalOptions.DisabledCharacter && requested.Value == disabled) throw new ArgumentException($"{name} equals the platform disabled byte; use DisabledCharacter.", name);
		state.ControlCharacters[index] = requested.Value == PtyTerminalOptions.DisabledCharacter ? disabled : (byte)requested.Value;
	}

	private static void Verify(TerminalConfiguration configuration, UnixTerminalState desired, UnixTerminalState actual, UnixTerminalConstants constants) {
		if (configuration.Profile == PtyTerminalProfile.Raw) {
			VerifyFlags("Raw input flags", actual.InputFlags, desired.InputFlags, constants.RawInputClear | constants.RawInputSet);
			VerifyFlags("Raw output flags", actual.OutputFlags, desired.OutputFlags, constants.OutputPostProcessing);
			VerifyFlags("Raw control flags", actual.ControlFlags, desired.ControlFlags, constants.RawControlClear | constants.RawControlSet);
			VerifyFlags("Raw local flags", actual.LocalFlags, desired.LocalFlags, constants.RawLocalClear & ~constants.TransientLocal);
			VerifyCharacter("Raw VMIN", actual, desired, constants.MinimumIndex);
			VerifyCharacter("Raw VTIME", actual, desired, constants.TimeoutIndex);
			return;
		}
		if (configuration.Echo.HasValue) {
			VerifyFlags("Echo", actual.LocalFlags, desired.LocalFlags, constants.Echo | constants.EchoNewline);
		}
		if (configuration.CanonicalInput.HasValue) VerifyFlags("CanonicalInput", actual.LocalFlags, desired.LocalFlags, constants.Canonical);
		if (configuration.SignalProcessing.HasValue) VerifyFlags("SignalProcessing", actual.LocalFlags, desired.LocalFlags, constants.SignalProcessing);
		if (configuration.InterruptCharacter.HasValue) VerifyCharacter("InterruptCharacter", actual, desired, constants.InterruptIndex);
		if (configuration.EndOfFileCharacter.HasValue) VerifyCharacter("EndOfFileCharacter", actual, desired, constants.EndOfFileIndex);
		if (configuration.EraseCharacter.HasValue) VerifyCharacter("EraseCharacter", actual, desired, constants.EraseIndex);
		if (configuration.MinimumReadBytes.HasValue) VerifyCharacter("MinimumReadBytes", actual, desired, constants.MinimumIndex);
		if (configuration.ReadTimeoutDeciseconds.HasValue) VerifyCharacter("ReadTimeoutDeciseconds", actual, desired, constants.TimeoutIndex);
	}
	private static void VerifyFlags(string field, ulong actual, ulong desired, ulong mask) {
		if ((actual & mask) != (desired & mask)) throw new IOException($"Terminal configuration readback did not honor {field}.");
	}
	private static void VerifyCharacter(string field, UnixTerminalState actual, UnixTerminalState desired, int index) {
		if (actual.ControlCharacters[index] != desired.ControlCharacters[index]) throw new IOException($"Terminal configuration readback did not honor {field}.");
	}
}
