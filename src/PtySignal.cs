namespace Icod.Pty;

/// <summary>Named Unix signals, independent of terminal input processing and native numeric signal values.</summary>
public enum PtySignal {
	/// <summary>Request hangup (SIGHUP).</summary>
	Hangup,
	/// <summary>Request interrupt (SIGINT).</summary>
	Interrupt,
	/// <summary>Request termination (SIGTERM), which the application may handle or ignore.</summary>
	Terminate,
	/// <summary>Request forced termination (SIGKILL).</summary>
	Kill
}
