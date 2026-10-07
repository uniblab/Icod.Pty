using Xunit;
using System.Text;
using System.Text.Json;

namespace Icod.Pty.Tests;

public sealed class ConPtyFragmentationProbeTests {
	[Fact]
	public void Patterns_cover_intact_every_query_split_and_bytewise_delays() {
		IReadOnlyList<ConPtyWritePattern> patterns = ConPtyFragmentationProbe.CreatePatterns();

		Assert.Equal(10, patterns.Count);
		Assert.Equal("intact-query", patterns[0].Name);
		Assert.Equal([1, 1, 1, 1, 1, 1, 8], patterns[0].FragmentLengths);
		Assert.Equal(0, patterns[0].DelayMilliseconds);
		for (int split = 1; split < 8; split++) {
			ConPtyWritePattern pattern = patterns[split];
			Assert.Equal($"query-split-{split}", pattern.Name);
			Assert.Equal([1, 1, 1, 1, 1, 1, split, 8 - split], pattern.FragmentLengths);
			Assert.Equal(0, pattern.DelayMilliseconds);
		}
		Assert.Equal("query-bytewise-0ms", patterns[8].Name);
		Assert.Equal(Enumerable.Repeat(1, 14), patterns[8].FragmentLengths);
		Assert.Equal(0, patterns[8].DelayMilliseconds);
		Assert.Equal("query-bytewise-10ms", patterns[9].Name);
		Assert.Equal(Enumerable.Repeat(1, 14), patterns[9].FragmentLengths);
		Assert.Equal(10, patterns[9].DelayMilliseconds);
		Assert.All(patterns, pattern => {
			Assert.Equal(14, pattern.FragmentLengths.Sum());
			Assert.Equal(Enumerable.Repeat(1, 6), pattern.FragmentLengths.Take(6));
		});
	}

	[Fact]
	public void Any_prefix_loss_or_mismatch_is_reproduced() {
		Assert.Equal(ConPtyProbeOutcome.Reproduced, ConPtyFragmentationProbe.Classify([
			Trial(ConPtyTrialOutcome.Exact, 14),
			Trial(ConPtyTrialOutcome.TimedOut, 6),
			Trial(ConPtyTrialOutcome.PrefixLost, 12)
		]));
		Assert.Equal(ConPtyProbeOutcome.Reproduced, ConPtyFragmentationProbe.Classify([
			Trial(ConPtyTrialOutcome.Exact, 14),
			Trial(ConPtyTrialOutcome.Mismatch, 9)
		]));
	}

	[Fact]
	public void All_exact_trials_are_not_reproduced() {
		Assert.Equal(ConPtyProbeOutcome.NotReproduced, ConPtyFragmentationProbe.Classify([
			Trial(ConPtyTrialOutcome.Exact, 14),
			Trial(ConPtyTrialOutcome.Exact, 14)
		]));
	}

	[Fact]
	public void Timeout_without_mismatch_is_inconclusive() {
		Assert.Equal(ConPtyProbeOutcome.Inconclusive, ConPtyFragmentationProbe.Classify([
			Trial(ConPtyTrialOutcome.Exact, 14),
			Trial(ConPtyTrialOutcome.TimedOut, 6)
		]));
	}

	[Fact]
	public void Report_overwrites_an_existing_file_without_payload_fields() {
		string directory = Path.Combine(Path.GetTempPath(), "icod-pty-conpty-report-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try {
			string path = Path.Combine(directory, "report.json");
			File.WriteAllText(path, "secret-marker");
			ConPtyTrialResult[] trials = [
				new("direct", "intact-query", 1, ConPtyTrialOutcome.Exact, 14),
				new("nested-sample", "query-split-2", 1, ConPtyTrialOutcome.PrefixLost, 12),
				new("direct", "query-bytewise-10ms", 1, ConPtyTrialOutcome.Mismatch, 8),
				new("nested-sample", "query-split-7", 1, ConPtyTrialOutcome.TimedOut, 6)
			];
			ConPtyProbeReport report = new(ConPtyFragmentationProbe.Schema, "Windows test", "10.0.test", "X64",
				".NET 10.0.test", 1, ConPtyProbeOutcome.Reproduced, trials);

			ConPtyFragmentationProbe.WriteReport(path, report);

			byte[] bytes = File.ReadAllBytes(path);
			Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
			using JsonDocument json = JsonDocument.Parse(bytes);
			JsonElement root = json.RootElement;
			Assert.Equal("icod-pty/conpty-fragmentation/v1", root.GetProperty("schema").GetString());
			Assert.Equal("Reproduced", root.GetProperty("outcome").GetString());
			JsonElement counts = root.GetProperty("counts");
			Assert.Equal(1, counts.GetProperty("exact").GetInt32());
			Assert.Equal(1, counts.GetProperty("prefixLost").GetInt32());
			Assert.Equal(1, counts.GetProperty("mismatch").GetInt32());
			Assert.Equal(1, counts.GetProperty("timedOut").GetInt32());
			Assert.Equal("PrefixLost", root.GetProperty("trials")[1].GetProperty("outcome").GetString());
			string text = Encoding.UTF8.GetString(bytes);
			Assert.DoesNotContain("secret-marker", text, StringComparison.Ordinal);
			Assert.DoesNotContain(Convert.ToHexString(Encoding.UTF8.GetBytes("雪\u001b[A\u001b[12;34R")), text, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("payload", text, StringComparison.OrdinalIgnoreCase);
			Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
		} finally { Directory.Delete(directory, true); }
	}

	[Fact]
	public void Report_rejects_a_missing_parent_directory() {
		string parent = Path.Combine(Path.GetTempPath(), "icod-pty-conpty-missing-" + Guid.NewGuid().ToString("N"));
		string path = Path.Combine(parent, "report.json");
		ConPtyProbeReport report = Report(ConPtyProbeOutcome.NotReproduced,
			[new("direct", "intact-query", 1, ConPtyTrialOutcome.Exact, 14)]);

		DirectoryNotFoundException error = Assert.Throws<DirectoryNotFoundException>(() =>
			ConPtyFragmentationProbe.WriteReport(path, report));

		Assert.Contains(parent, error.Message, StringComparison.Ordinal);
		Assert.False(Directory.Exists(parent));
	}

	[Fact]
	public void Proper_query_suffix_after_control_prefix_is_prefix_loss() {
		byte[] expected = Encoding.UTF8.GetBytes("雪\u001b[A\u001b[12;34R");
		byte[] received = [.. expected.AsSpan(0, 6).ToArray(), .. expected.AsSpan(8).ToArray()];

		Assert.Equal(ConPtyTrialOutcome.PrefixLost, ConPtyFragmentationProbe.ClassifyTrace(received, timedOut: true));
	}

	[Fact]
	public void Unrelated_partial_trace_is_mismatch() {
		byte[] expected = Encoding.UTF8.GetBytes("雪\u001b[A\u001b[12;34R");
		byte[] received = [.. expected.AsSpan(0, 6).ToArray(), 0x55];

		Assert.Equal(ConPtyTrialOutcome.Mismatch, ConPtyFragmentationProbe.ClassifyTrace(received, timedOut: true));
	}

	[Fact]
	public void Empty_or_inconsistent_reports_are_rejected() {
		Assert.Throws<ArgumentException>(() => ConPtyFragmentationProbe.Classify([]));
		ConPtyProbeReport inconsistent = Report(ConPtyProbeOutcome.NotReproduced,
			[new("direct", "intact-query", 1, ConPtyTrialOutcome.PrefixLost, 12)]);

		Assert.Throws<ArgumentException>(() => ConPtyFragmentationProbe.WriteReport(
			Path.Combine(Path.GetTempPath(), "unused-" + Guid.NewGuid().ToString("N") + ".json"), inconsistent));
	}

	[Fact]
	public async Task Cancellation_during_fragment_delay_propagates_and_deletes_trace() {
		using CancellationTokenSource cancellation = new();
		FakeTrialProcess? process = null;
		string? trace = null;
		ConPtyWritePattern pattern = new("cancel-delay", [1, 13], 1000);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConPtyFragmentationProbe.RunNativeTrialAsync(
			"direct", pattern, 1, cancellation.Token, (hostPath, tracePath, token) => {
				trace = tracePath;
				process = new FakeTrialProcess(tracePath, cancellation.Cancel);
				return Task.FromResult<IConPtyTrialProcess>(process);
			}, TimeSpan.FromMilliseconds(200)));

		Assert.NotNull(process);
		Assert.True(process.Disposed);
		Assert.NotNull(trace);
		Assert.False(File.Exists(trace));
	}

	[Fact]
	public async Task Trial_timeout_records_received_count_and_disposes_process() {
		FakeTrialProcess? process = null;
		string? trace = null;
		ConPtyWritePattern pattern = new("timeout", [14], 0);

		ConPtyTrialResult result = await ConPtyFragmentationProbe.RunNativeTrialAsync(
			"nested-sample", pattern, 3, default, (hostPath, tracePath, token) => {
				trace = tracePath;
				process = new FakeTrialProcess(tracePath);
				return Task.FromResult<IConPtyTrialProcess>(process);
			}, TimeSpan.FromMilliseconds(30));

		Assert.Equal(new("nested-sample", "timeout", 3, ConPtyTrialOutcome.TimedOut, 14), result);
		Assert.NotNull(process);
		Assert.True(process.Disposed);
		Assert.NotNull(trace);
		Assert.False(File.Exists(trace));
	}

	[Fact]
	public async Task Unavailable_platform_has_no_trials() {
		if (OperatingSystem.IsWindows()) return;

		ConPtyProbeReport report = await ConPtyFragmentationProbe.RunAsync(5, default);

		Assert.Equal(ConPtyProbeOutcome.Unavailable, report.Outcome);
		Assert.Empty(report.Trials);
		Assert.Equal(5, report.Repetitions);
	}

	[Fact]
	public async Task Injected_runner_covers_every_host_pattern_and_attempt() {
		List<(string HostPath, string Pattern, int Attempt)> calls = [];
		ConPtyTrialExecutor executor = (hostPath, pattern, attempt, cancellationToken) => {
			calls.Add((hostPath, pattern.Name, attempt));
			return Task.FromResult(new ConPtyTrialResult(hostPath, pattern.Name, attempt, ConPtyTrialOutcome.Exact, 14));
		};

		ConPtyProbeReport report = await ConPtyFragmentationProbe.RunAsync(2, default, executor);

		Assert.Equal(40, calls.Count);
		Assert.Equal(40, calls.Distinct().Count());
		Assert.Equal(["direct", "nested-sample"], calls.Select(call => call.HostPath).Distinct());
		Assert.Equal(10, calls.Select(call => call.Pattern).Distinct().Count());
		Assert.Equal([1, 2], calls.Select(call => call.Attempt).Distinct());
		Assert.Equal(ConPtyProbeOutcome.NotReproduced, report.Outcome);
		Assert.Equal(calls.Count, report.Trials.Count);
	}

	private static ConPtyTrialResult Trial(ConPtyTrialOutcome outcome, int receivedByteCount) =>
		new("direct", "intact-query", 1, outcome, receivedByteCount);

	private static ConPtyProbeReport Report(ConPtyProbeOutcome outcome, IReadOnlyList<ConPtyTrialResult> trials) =>
		new(ConPtyFragmentationProbe.Schema, "Windows test", "10.0.test", "X64", ".NET 10.0.test", 1, outcome, trials);

	private sealed class FakeTrialProcess : IConPtyTrialProcess {
		internal FakeTrialProcess(string tracePath, Action? afterFirstWrite = null) {
			Input = new FlushAfterWriteStream(tracePath, afterFirstWrite);
			Output = new ReadyThenWaitStream();
		}
		public Stream Input { get; }
		public Stream Output { get; }
		internal bool Disposed { get; private set; }
		public async ValueTask DisposeAsync() {
			if (Disposed) return;
			Disposed = true;
			await Input.DisposeAsync();
			await Output.DisposeAsync();
		}
	}

	private sealed class FlushAfterWriteStream(string path, Action? afterFirstWrite) : Stream {
		private readonly FileStream file = new(path, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete, 1, FileOptions.Asynchronous);
		private Action? afterWrite = afterFirstWrite;
		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => file.Length;
		public override long Position { get => file.Position; set => throw new NotSupportedException(); }
		public override void Flush() => file.Flush();
		public override Task FlushAsync(CancellationToken cancellationToken) => file.FlushAsync(cancellationToken);
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) { file.Write(buffer, offset, count); file.Flush(); Interlocked.Exchange(ref afterWrite, null)?.Invoke(); }
		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
			await file.WriteAsync(buffer, cancellationToken);
			await file.FlushAsync(cancellationToken);
			Interlocked.Exchange(ref afterWrite, null)?.Invoke();
		}
		protected override void Dispose(bool disposing) { if (disposing) file.Dispose(); base.Dispose(disposing); }
		public override async ValueTask DisposeAsync() { await file.DisposeAsync(); GC.SuppressFinalize(this); }
	}

	private sealed class ReadyThenWaitStream : Stream {
		private readonly byte[] ready = Encoding.UTF8.GetBytes("RAW-READY");
		private int offset;
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int bufferOffset, int count) => throw new NotSupportedException();
		public override long Seek(long value, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int bufferOffset, int count) => throw new NotSupportedException();
		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
			if (offset < ready.Length) {
				int count = Math.Min(buffer.Length, ready.Length - offset);
				ready.AsMemory(offset, count).CopyTo(buffer);
				offset += count;
				return count;
			}
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return 0;
		}
	}
}
