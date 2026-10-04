using System.Runtime.ExceptionServices;

namespace Icod.Pty;

internal static class CleanupActions {
	internal static void AfterFailure(Exception original, params Action[] actions) {
		try { Run(actions); }
		catch (Exception cleanup) { throw new AggregateException("PTY startup failed and cleanup also failed.", original, cleanup); }
	}
	internal static void Run(params Action[] actions) {
		List<Exception>? failures = null;
		foreach (Action action in actions) { try { action(); } catch (Exception error) { (failures ??= []).Add(error); } }
		if (failures?.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
		if (failures?.Count > 1) throw new AggregateException("Multiple PTY cleanup operations failed.", failures);
	}
}
