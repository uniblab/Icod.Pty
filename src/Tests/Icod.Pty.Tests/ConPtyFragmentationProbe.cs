using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;

namespace Icod.Pty.Tests;

internal enum ConPtyTrialOutcome { Exact, PrefixLost, Mismatch, TimedOut }

internal enum ConPtyProbeOutcome { Reproduced, NotReproduced, Inconclusive, Unavailable }

internal sealed record ConPtyWritePattern(string Name, int[] FragmentLengths, int DelayMilliseconds);

internal sealed record ConPtyTrialResult(
	string HostPath,
	string Pattern,
	int Attempt,
	ConPtyTrialOutcome Outcome,
	int ReceivedByteCount);

internal sealed record ConPtyProbeReport(
	string Schema,
	string OSDescription,
	string OSVersion,
	string Architecture,
	string Framework,
	int Repetitions,
	ConPtyProbeOutcome Outcome,
	IReadOnlyList<ConPtyTrialResult> Trials);

internal delegate Task<ConPtyTrialResult> ConPtyTrialExecutor(
	string hostPath,
	ConPtyWritePattern pattern,
	int attempt,
	CancellationToken cancellationToken);

internal interface IConPtyTrialProcess : IAsyncDisposable {
	Stream Input { get; }
	Stream Output { get; }
}

internal delegate Task<IConPtyTrialProcess> ConPtyTrialProcessFactory(
	string hostPath,
	string tracePath,
	CancellationToken cancellationToken);

internal static class ConPtyFragmentationProbe {
	internal const string Schema = "icod-pty/conpty-fragmentation/v1";
	private static readonly byte[] Sequence = Encoding.UTF8.GetBytes("雪\u001b[A\u001b[12;34R");
	private static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};
	private sealed record Counts(int Exact, int PrefixLost, int Mismatch, int TimedOut);
	private sealed record ReportDocument(
		string Schema,
		string OSDescription,
		string OSVersion,
		string Architecture,
		string Framework,
		int Repetitions,
		ConPtyProbeOutcome Outcome,
		Counts Counts,
		IReadOnlyList<ConPtyTrialResult> Trials);

	internal static IReadOnlyList<ConPtyWritePattern> CreatePatterns() {
		List<ConPtyWritePattern> patterns = [new("intact-query", [1, 1, 1, 1, 1, 1, 8], 0)];
		for (int split = 1; split < 8; split++)
			patterns.Add(new($"query-split-{split}", [1, 1, 1, 1, 1, 1, split, 8 - split], 0));
		patterns.Add(new("query-bytewise-0ms", Enumerable.Repeat(1, Sequence.Length).ToArray(), 0));
		patterns.Add(new("query-bytewise-10ms", Enumerable.Repeat(1, Sequence.Length).ToArray(), 10));
		return patterns;
	}

	internal static ConPtyProbeOutcome Classify(IReadOnlyList<ConPtyTrialResult> trials) {
		ArgumentNullException.ThrowIfNull(trials);
		if (trials.Count == 0) throw new ArgumentException("At least one ConPTY trial is required.", nameof(trials));
		bool timedOut = false;
		foreach (ConPtyTrialResult trial in trials) {
			ArgumentNullException.ThrowIfNull(trial);
			if (string.IsNullOrWhiteSpace(trial.HostPath) || string.IsNullOrWhiteSpace(trial.Pattern) || trial.Attempt < 1 ||
				trial.ReceivedByteCount < 0 || trial.ReceivedByteCount > Sequence.Length || !Enum.IsDefined(trial.Outcome) ||
				trial.Outcome == ConPtyTrialOutcome.Exact && trial.ReceivedByteCount != Sequence.Length)
				throw new ArgumentException("The ConPTY trial is inconsistent.", nameof(trials));
			if (trial.Outcome is ConPtyTrialOutcome.PrefixLost or ConPtyTrialOutcome.Mismatch)
				return ConPtyProbeOutcome.Reproduced;
			timedOut |= trial.Outcome == ConPtyTrialOutcome.TimedOut;
		}
		return timedOut ? ConPtyProbeOutcome.Inconclusive : ConPtyProbeOutcome.NotReproduced;
	}

	internal static ConPtyTrialOutcome ClassifyTrace(ReadOnlySpan<byte> received, bool timedOut) {
		if (received.SequenceEqual(Sequence)) return timedOut ? ConPtyTrialOutcome.TimedOut : ConPtyTrialOutcome.Exact;
		ReadOnlySpan<byte> controlPrefix = Sequence.AsSpan(0, 6);
		ReadOnlySpan<byte> query = Sequence.AsSpan(6);
		if (received.Length > controlPrefix.Length && received.StartsWith(controlPrefix)) {
			ReadOnlySpan<byte> receivedQuery = received[controlPrefix.Length..];
			if (receivedQuery.Length < query.Length && query.EndsWith(receivedQuery))
				return ConPtyTrialOutcome.PrefixLost;
		}
		if (timedOut && Sequence.AsSpan().StartsWith(received)) return ConPtyTrialOutcome.TimedOut;
		return ConPtyTrialOutcome.Mismatch;
	}

	internal static async Task<ConPtyProbeReport> RunAsync(int repetitions, CancellationToken cancellationToken,
		ConPtyTrialExecutor? executor = null) {
		if (repetitions is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(repetitions), "Repetitions must be between 1 and 20.");
		cancellationToken.ThrowIfCancellationRequested();
		if (executor is null && !OperatingSystem.IsWindows()) return CreateReport(repetitions, ConPtyProbeOutcome.Unavailable, []);
		executor ??= ExecuteNativeTrialAsync;
		List<ConPtyTrialResult> trials = [];
		foreach (string hostPath in new[] { "direct", "nested-sample" }) {
			foreach (ConPtyWritePattern pattern in CreatePatterns()) {
				for (int attempt = 1; attempt <= repetitions; attempt++) {
					cancellationToken.ThrowIfCancellationRequested();
					ConPtyTrialResult trial = await executor(hostPath, pattern, attempt, cancellationToken).ConfigureAwait(false);
					if (trial.HostPath != hostPath || trial.Pattern != pattern.Name || trial.Attempt != attempt)
						throw new InvalidOperationException("The ConPTY trial executor returned mismatched coordinates.");
					trials.Add(trial);
				}
			}
		}
		return CreateReport(repetitions, Classify(trials), trials);
	}

	internal static async Task<ConPtyTrialResult> RunNativeTrialAsync(string hostPath, ConPtyWritePattern pattern,
		int attempt, CancellationToken cancellationToken, ConPtyTrialProcessFactory? processFactory = null,
		TimeSpan? operationTimeout = null) {
		ArgumentNullException.ThrowIfNull(pattern);
		if (hostPath is not ("direct" or "nested-sample")) throw new ArgumentOutOfRangeException(nameof(hostPath));
		if (string.IsNullOrWhiteSpace(pattern.Name) || pattern.FragmentLengths.Length == 0 ||
			pattern.FragmentLengths.Any(length => length < 1) || pattern.FragmentLengths.Sum() != Sequence.Length ||
			pattern.DelayMilliseconds is < 0 or > 1000)
			throw new ArgumentException("The ConPTY write pattern is inconsistent.", nameof(pattern));
		if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
		TimeSpan timeout = operationTimeout ?? TimeSpan.FromSeconds(2);
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(2)) throw new ArgumentOutOfRangeException(nameof(operationTimeout));
		processFactory ??= StartNativeProcessAsync;
		string tracePath = Path.Combine(Path.GetTempPath(), "icod-pty-conpty-" + Guid.NewGuid().ToString("N"));
		try {
			await using IConPtyTrialProcess process = await processFactory(hostPath, tracePath, cancellationToken).ConfigureAwait(false);
			if (!await ReadUntilAsync(process.Output, "RAW-READY", timeout, cancellationToken).ConfigureAwait(false))
				throw new TimeoutException("The ConPTY trial child did not become ready within the bounded deadline.");
			int offset = 0;
			for (int index = 0; index < pattern.FragmentLengths.Length; index++) {
				int length = pattern.FragmentLengths[index];
				await process.Input.WriteAsync(Sequence.AsMemory(offset, length), cancellationToken).ConfigureAwait(false);
				offset += length;
				if (index + 1 < pattern.FragmentLengths.Length && pattern.DelayMilliseconds != 0)
					await Task.Delay(pattern.DelayMilliseconds, cancellationToken).ConfigureAwait(false);
			}
			bool completed = await ReadUntilAsync(process.Output, "SEQUENCE:" + Convert.ToHexString(Sequence), timeout, cancellationToken).ConfigureAwait(false);
			byte[] received = ReadTrace(tracePath);
			ConPtyTrialOutcome outcome = ClassifyTrace(received, timedOut: !completed);
			if (completed) await process.Input.WriteAsync(new byte[] { 4 }, cancellationToken).ConfigureAwait(false);
			return new(hostPath, pattern.Name, attempt, outcome, received.Length);
		} finally { File.Delete(tracePath); }
	}

	private static Task<ConPtyTrialResult> ExecuteNativeTrialAsync(string hostPath, ConPtyWritePattern pattern,
		int attempt, CancellationToken cancellationToken) =>
		RunNativeTrialAsync(hostPath, pattern, attempt, cancellationToken);

	private static async Task<IConPtyTrialProcess> StartNativeProcessAsync(string hostPath, string tracePath,
		CancellationToken cancellationToken) {
		string[] arguments = ["raw-sequence", Sequence.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), tracePath, "4096"];
		PtyStartInfo startInfo = hostPath == "direct" ? PtyTestSupport.Child(arguments) : InteractiveSampleTests.Sample(arguments);
		return new NativeTrialProcess(await PtyProcess.StartAsync(startInfo, cancellationToken).ConfigureAwait(false));
	}

	private static async Task<bool> ReadUntilAsync(Stream output, string marker, TimeSpan timeout,
		CancellationToken cancellationToken) {
		byte[] expected = Encoding.UTF8.GetBytes(marker);
		int matched = 0;
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(timeout);
		byte[] one = new byte[1];
		try {
			while (true) {
				int count = await output.ReadAsync(one, deadline.Token).ConfigureAwait(false);
				if (count == 0) return false;
				matched = one[0] == expected[matched] ? matched + 1 : one[0] == expected[0] ? 1 : 0;
				if (matched == expected.Length) return true;
			}
		} catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested) {
			return false;
		}
	}

	private static byte[] ReadTrace(string path) {
		using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using MemoryStream bytes = new();
		file.CopyTo(bytes);
		return bytes.ToArray();
	}

	private static ConPtyProbeReport CreateReport(int repetitions, ConPtyProbeOutcome outcome,
		IReadOnlyList<ConPtyTrialResult> trials) => new(
		Schema,
		RuntimeInformation.OSDescription,
		Environment.OSVersion.VersionString,
		RuntimeInformation.ProcessArchitecture.ToString(),
		RuntimeInformation.FrameworkDescription,
		repetitions,
		outcome,
		trials);

	private sealed class NativeTrialProcess(PtyProcess process) : IConPtyTrialProcess {
		public Stream Input => process.Input;
		public Stream Output => process.Output;
		public ValueTask DisposeAsync() => process.DisposeAsync();
	}

	internal static void WriteReport(string path, ConPtyProbeReport report) {
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(report);
		ValidateReport(report);
		string fullPath = Path.GetFullPath(path);
		string? directory = Path.GetDirectoryName(fullPath);
		if (directory is null || !Directory.Exists(directory))
			throw new DirectoryNotFoundException($"ConPTY report directory does not exist: '{directory ?? fullPath}'.");
		Counts counts = new(
			report.Trials.Count(trial => trial.Outcome == ConPtyTrialOutcome.Exact),
			report.Trials.Count(trial => trial.Outcome == ConPtyTrialOutcome.PrefixLost),
			report.Trials.Count(trial => trial.Outcome == ConPtyTrialOutcome.Mismatch),
			report.Trials.Count(trial => trial.Outcome == ConPtyTrialOutcome.TimedOut));
		ReportDocument document = new(report.Schema, report.OSDescription, report.OSVersion, report.Architecture,
			report.Framework, report.Repetitions, report.Outcome, counts, report.Trials);
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
		string temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
		try {
			File.WriteAllBytes(temporary, bytes);
			File.Move(temporary, fullPath, true);
		} finally {
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

	private static void ValidateReport(ConPtyProbeReport report) {
		if (!string.Equals(report.Schema, Schema, StringComparison.Ordinal) ||
			string.IsNullOrWhiteSpace(report.OSDescription) || string.IsNullOrWhiteSpace(report.OSVersion) ||
			string.IsNullOrWhiteSpace(report.Architecture) || string.IsNullOrWhiteSpace(report.Framework) ||
			report.Repetitions < 1 || !Enum.IsDefined(report.Outcome) || report.Trials is null)
			throw new ArgumentException("The ConPTY report is inconsistent.", nameof(report));
		if (report.Outcome == ConPtyProbeOutcome.Unavailable) {
			if (report.Trials.Count != 0) throw new ArgumentException("An unavailable ConPTY report cannot contain trials.", nameof(report));
			return;
		}
		if (report.Outcome != Classify(report.Trials) || report.Trials.Any(trial => trial.Attempt > report.Repetitions))
			throw new ArgumentException("The ConPTY report outcome or repetition count is inconsistent.", nameof(report));
	}
}
