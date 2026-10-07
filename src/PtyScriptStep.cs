using Icod.Pty.Session;

namespace Icod.Pty;

/// <summary>Identifies a scripted PTY operation.</summary>
public enum PtyScriptStepKind {
	/// <summary>Write bytes to the child.</summary>
	Send,
	/// <summary>Wait for output bytes.</summary>
	Expect
}

/// <summary>Defines one immutable send or expect operation.</summary>
public sealed class PtyScriptStep {
	private PtyScriptStep(PtyScriptStepKind kind, ReadOnlyMemory<byte> bytes, TimeSpan timeout) {
		Kind = kind; Bytes = bytes.ToArray(); Timeout = timeout;
	}
	/// <summary>Gets the operation kind.</summary>
	public PtyScriptStepKind Kind { get; }
	/// <summary>Gets the copied bytes to send or match.</summary>
	public ReadOnlyMemory<byte> Bytes { get; }
	/// <summary>Gets the expectation timeout, or zero for a send.</summary>
	public TimeSpan Timeout { get; }
	/// <summary>Creates a send operation. Empty sends are allowed.</summary>
	public static PtyScriptStep Send(ReadOnlyMemory<byte> bytes) => new(PtyScriptStepKind.Send, bytes, TimeSpan.Zero);
	/// <summary>Creates an expectation with a finite positive deadline.</summary>
	public static PtyScriptStep Expect(ReadOnlyMemory<byte> pattern, TimeSpan timeout) {
		AutomationConfiguration.ValidateExpectation(pattern, timeout, int.MaxValue);
		return new(PtyScriptStepKind.Expect, pattern, timeout);
	}
}
