namespace Icod.Pty;

/// <summary>Selects the process scope established before the application is started.</summary>
public enum PtyProcessOwnership {
	/// <summary>Own only the primary process. This is the default.</summary>
	PrimaryProcess,
	/// <summary>Own a Windows job or the Unix initial process group; this is not universal descendant containment.</summary>
	PlatformScope
}
