using System.Globalization;
using System.Text.RegularExpressions;

namespace VoicePromptTray;

internal sealed record PerformanceSample(
    DateTimeOffset? Timestamp,
    int? MicrophoneReadyMs,
    double? RecordingSeconds,
    double PrimarySeconds,
    double RetrySeconds,
    double TotalSeconds,
    double ComputeSeconds,
    string Language,
    double Confidence,
    int Segments,
    int BufferedBatches,
    bool UsedFullFallback);

internal sealed class PerformanceSnapshot
{
    private static readonly Regex TimestampPattern = new(
        @"^(?<value>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MicrophonePattern = new(
        @"Audio capture ready in (?<value>\d+) ms",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RecordingPattern = new(
        @"Recording stopped \((?<value>\d+(?:\.\d+)?)s\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LatencyPattern = new(
        @"Transcription latency: primary (?<primary>\d+(?:\.\d+)?)s, retry (?<retry>\d+(?:\.\d+)?)s, total (?<total>\d+(?:\.\d+)?)s",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LanguagePattern = new(
        @"whisper_dictation\.engine\.local: Detected language: (?<language>[a-z]{2,3}) \(conf (?<confidence>\d+(?:\.\d+)?)\) \[(?<segments>\d+) segments\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BufferedPattern = new(
        @"Buffered transcription ready: batches=(?<batches>\d+), prefetched=(?<prefetched>\d+), compute=(?<compute>\d+(?:\.\d+)?)s, release_wait=(?<wait>\d+(?:\.\d+)?)s, fallback=(?<fallback>True|False)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static PerformanceSnapshot Empty { get; } = new(Array.Empty<PerformanceSample>());

    public IReadOnlyList<PerformanceSample> Samples { get; }
    public int Count => Samples.Count;
    public PerformanceSample? Latest => Samples.LastOrDefault();
    public double MedianTotalSeconds => Percentile(Samples.Select(sample => sample.TotalSeconds), 0.50);
    public double P95TotalSeconds => Percentile(Samples.Select(sample => sample.TotalSeconds), 0.95);
    public double? MedianMicrophoneMs => NullablePercentile(
        Samples.Where(sample => sample.MicrophoneReadyMs.HasValue).Select(sample => (double)sample.MicrophoneReadyMs!.Value),
        0.50);
    public int RetryCount => Samples.Count(sample => sample.RetrySeconds > 0.0005);
    public int FullFallbackCount => Samples.Count(sample => sample.UsedFullFallback);
    public double? MedianRealtimeSpeed => NullablePercentile(
        Samples.Where(sample => sample.RecordingSeconds > 0 && sample.ComputeSeconds > 0)
            .Select(sample => sample.RecordingSeconds!.Value / sample.ComputeSeconds),
        0.50);

    private PerformanceSnapshot(IReadOnlyList<PerformanceSample> samples) => Samples = samples;

    public static PerformanceSnapshot Read(string path, int maxSamples = 50, int maxBytes = 1_048_576)
    {
        if (!File.Exists(path))
            return Empty;

        try
        {
            return Parse(ReadTailLines(path, maxBytes), maxSamples);
        }
        catch (IOException)
        {
            return Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return Empty;
        }
        catch (FormatException)
        {
            return Empty;
        }
        catch (OverflowException)
        {
            return Empty;
        }
    }

    internal static PerformanceSnapshot Parse(IEnumerable<string> lines, int maxSamples = 50)
    {
        maxSamples = Math.Max(1, maxSamples);
        var samples = new List<PerformanceSample>();
        var pending = new PendingSample();

        foreach (string line in lines)
        {
            if (line.Contains("Recording started", StringComparison.Ordinal))
                pending = new PendingSample { Timestamp = ParseTimestamp(line) };

            Match microphone = MicrophonePattern.Match(line);
            if (microphone.Success)
                pending.MicrophoneReadyMs = ParseInt(microphone.Groups["value"].Value);

            Match recording = RecordingPattern.Match(line);
            if (recording.Success)
            {
                pending.RecordingSeconds = ParseDouble(recording.Groups["value"].Value);
                pending.Released = true;
            }

            Match latency = LatencyPattern.Match(line);
            if (latency.Success)
            {
                pending.Timestamp ??= ParseTimestamp(line);
                double primary = ParseDouble(latency.Groups["primary"].Value);
                double retry = ParseDouble(latency.Groups["retry"].Value);
                double total = ParseDouble(latency.Groups["total"].Value);
                pending.AccumulatedPrimarySeconds += primary;
                pending.AccumulatedRetrySeconds += retry;
                pending.AccumulatedComputeSeconds += total;
                if (pending.Released)
                {
                    pending.PrimarySeconds = primary;
                    pending.RetrySeconds = retry;
                    pending.TotalSeconds = total;
                    pending.ComputeSeconds = total;
                    pending.HasLatency = true;
                }
            }

            Match language = LanguagePattern.Match(line);
            if (language.Success)
            {
                pending.Language = language.Groups["language"].Value;
                pending.Confidence = ParseDouble(language.Groups["confidence"].Value);
                pending.Segments += ParseInt(language.Groups["segments"].Value);
            }

            Match buffered = BufferedPattern.Match(line);
            if (buffered.Success)
            {
                pending.PrimarySeconds = pending.AccumulatedPrimarySeconds;
                pending.RetrySeconds = pending.AccumulatedRetrySeconds;
                pending.TotalSeconds = ParseDouble(buffered.Groups["wait"].Value);
                pending.ComputeSeconds = ParseDouble(buffered.Groups["compute"].Value);
                pending.BufferedBatches = ParseInt(buffered.Groups["batches"].Value);
                pending.UsedFullFallback = buffered.Groups["fallback"].Value == "True";
                pending.HasLatency = true;
            }

            bool delivered = line.Contains("Paste shortcut sent:", StringComparison.Ordinal) ||
                line.Contains("Transcript copied to clipboard:", StringComparison.Ordinal);
            if (!delivered ||
                !pending.Released ||
                !pending.HasLatency ||
                string.IsNullOrEmpty(pending.Language))
                continue;

            samples.Add(new PerformanceSample(
                pending.Timestamp ?? ParseTimestamp(line),
                pending.MicrophoneReadyMs,
                pending.RecordingSeconds,
                pending.PrimarySeconds,
                pending.RetrySeconds,
                pending.TotalSeconds,
                pending.ComputeSeconds,
                pending.Language,
                pending.Confidence,
                pending.Segments,
                pending.BufferedBatches,
                pending.UsedFullFallback));
            pending = new PendingSample();
        }

        if (samples.Count > maxSamples)
            samples = samples[^maxSamples..];
        return samples.Count == 0 ? Empty : new PerformanceSnapshot(samples);
    }

    internal static double Percentile(IEnumerable<double> values, double percentile)
    {
        double[] ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
            return 0;
        int index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }

    private static double? NullablePercentile(IEnumerable<double> values, double percentile)
    {
        double[] materialized = values.ToArray();
        return materialized.Length == 0 ? null : Percentile(materialized, percentile);
    }

    private static IEnumerable<string> ReadTailLines(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, stream.Length - Math.Max(4096, maxBytes));
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        if (start > 0)
            reader.ReadLine();
        while (reader.ReadLine() is { } line)
            yield return line;
    }

    private static DateTimeOffset? ParseTimestamp(string line)
    {
        Match match = TimestampPattern.Match(line);
        return match.Success && DateTimeOffset.TryParseExact(
            match.Groups["value"].Value,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTimeOffset value)
            ? value
            : null;
    }

    private static double ParseDouble(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);

    private sealed class PendingSample
    {
        public DateTimeOffset? Timestamp { get; set; }
        public int? MicrophoneReadyMs { get; set; }
        public double? RecordingSeconds { get; set; }
        public double PrimarySeconds { get; set; }
        public double RetrySeconds { get; set; }
        public double TotalSeconds { get; set; }
        public double ComputeSeconds { get; set; }
        public double AccumulatedPrimarySeconds { get; set; }
        public double AccumulatedRetrySeconds { get; set; }
        public double AccumulatedComputeSeconds { get; set; }
        public string Language { get; set; } = "";
        public double Confidence { get; set; }
        public int Segments { get; set; }
        public int BufferedBatches { get; set; }
        public bool UsedFullFallback { get; set; }
        public bool Released { get; set; }
        public bool HasLatency { get; set; }
    }
}
