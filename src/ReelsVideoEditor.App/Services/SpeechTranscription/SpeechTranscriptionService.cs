using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace ReelsVideoEditor.App.Services.SpeechTranscription;

public sealed record TranscriptionWord(string Word, TimeSpan Start, TimeSpan End);

public sealed record TranscriptionChunk(string Text, TimeSpan Start, TimeSpan End);

public sealed class SpeechTranscriptionService
{
    private const int WordsPerChunk = 3;
    private readonly WhisperModelManager modelManager = new();

    public async Task<IReadOnlyList<TranscriptionChunk>> TranscribeAsync(
        IReadOnlyList<AudioInputForTranscription> audioInputs,
        IProgress<TranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (audioInputs.Count == 0)
        {
            return [];
        }

        // Step 1: Ensure model is downloaded
        progress?.Report(new TranscriptionProgress("Pobieranie modelu Whisper...", 0));
        await modelManager.EnsureModelAsync(
            new Progress<double>(pct =>
                progress?.Report(new TranscriptionProgress("Pobieranie modelu Whisper...", pct * 0.2))),
            cancellationToken);

        // Step 2: Extract & mix audio to 16kHz mono WAV
        progress?.Report(new TranscriptionProgress("Ekstrakcja audio...", 20));
        var tempDir = Path.Combine(Path.GetTempPath(), "ReelsVideoEditor", "transcription_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var wavPath = Path.Combine(tempDir, "mixed_audio.wav");

        try
        {
            await ExtractMixedAudioAsync(audioInputs, wavPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(wavPath) || new FileInfo(wavPath).Length < 100)
            {
                return [];
            }

            // Step 3: Run Whisper transcription
            progress?.Report(new TranscriptionProgress("Transkrypcja mowy...", 40));
            var segments = await RunWhisperAsync(wavPath, progress, cancellationToken);

            // Step 4: Group into 3-word chunks
            progress?.Report(new TranscriptionProgress("Generowanie napisów...", 90));
            var chunks = GroupIntoChunks(segments);

            progress?.Report(new TranscriptionProgress("Gotowe!", 100));
            return chunks;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private async Task ExtractMixedAudioAsync(
        IReadOnlyList<AudioInputForTranscription> audioInputs,
        string outputWavPath,
        CancellationToken cancellationToken)
    {
        var ffmpegPath = ResolveFFmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new FileNotFoundException("Could not find ffmpeg.exe.");
        }

        string arguments;

        if (audioInputs.Count == 1)
        {
            var input = audioInputs[0];
            var sourceStart = Math.Max(0, input.SourceStartSeconds).ToString(CultureInfo.InvariantCulture);
            var duration = Math.Max(0.001, input.DurationSeconds).ToString(CultureInfo.InvariantCulture);

            arguments = $"-y -i \"{input.Path}\" -ss {sourceStart} -t {duration} -ar 16000 -ac 1 -sample_fmt s16 \"{outputWavPath}\"";
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("-y ");
            foreach (var input in audioInputs)
            {
                sb.Append($"-i \"{input.Path}\" ");
            }

            sb.Append("-filter_complex \"");
            for (var i = 0; i < audioInputs.Count; i++)
            {
                var a = audioInputs[i];
                var delayMs = (int)Math.Round(a.StartSeconds * 1000, MidpointRounding.AwayFromZero);
                var sourceStart = Math.Max(0, a.SourceStartSeconds).ToString(CultureInfo.InvariantCulture);
                var sourceDuration = Math.Max(0.001, a.DurationSeconds).ToString(CultureInfo.InvariantCulture);
                sb.Append($"[{i}:a]atrim=start={sourceStart}:duration={sourceDuration},asetpts=PTS-STARTPTS,adelay={delayMs}|{delayMs}[a{i}];");
            }

            for (var i = 0; i < audioInputs.Count; i++)
            {
                sb.Append($"[a{i}]");
            }

            sb.Append($"amix=inputs={audioInputs.Count}:duration=longest:dropout_transition=2[outa]\" ");
            sb.Append($"-map \"[outa]\" -ar 16000 -ac 1 -sample_fmt s16 \"{outputWavPath}\"");
            arguments = sb.ToString();
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var stderrLines = new List<string>();
        while (!process.StandardError.EndOfStream)
        {
            var line = await process.StandardError.ReadLineAsync(cancellationToken);
            if (line is not null)
            {
                stderrLines.Add(line);
                if (stderrLines.Count > 50)
                {
                    stderrLines.RemoveAt(0);
                }
            }
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var errorDetail = stderrLines.Count > 0
                ? string.Join(Environment.NewLine, stderrLines.TakeLast(20))
                : "No stderr output.";
            throw new InvalidOperationException(
                $"FFmpeg audio extraction failed (exit code {process.ExitCode}).{Environment.NewLine}{errorDetail}");
        }
    }

    private async Task<List<TranscriptionWord>> RunWhisperAsync(
        string wavPath,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var words = new List<TranscriptionWord>();

        using var whisperFactory = WhisperFactory.FromPath(modelManager.ModelPath);
        using var processor = whisperFactory.CreateBuilder()
            .WithLanguage("pl")
            .WithTokenTimestamps()
            .Build();

        await using var fileStream = File.OpenRead(wavPath);

        var segmentCount = 0;
        await foreach (var result in processor.ProcessAsync(fileStream, cancellationToken))
        {
            segmentCount++;
            var pct = 40 + Math.Min(segmentCount * 2, 48);
            progress?.Report(new TranscriptionProgress("Transkrypcja mowy...", pct));

            var text = result.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // Split segment text into individual words with interpolated timestamps
            var segmentWords = SplitSegmentIntoWords(text, result.Start, result.End);
            words.AddRange(segmentWords);
        }

        return words;
    }

    private static List<TranscriptionWord> SplitSegmentIntoWords(string text, TimeSpan segStart, TimeSpan segEnd)
    {
        var rawWords = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToArray();

        if (rawWords.Length == 0)
        {
            return [];
        }

        var totalDuration = segEnd - segStart;
        if (totalDuration <= TimeSpan.Zero)
        {
            totalDuration = TimeSpan.FromMilliseconds(100);
        }

        var wordDuration = totalDuration / rawWords.Length;
        var words = new List<TranscriptionWord>(rawWords.Length);

        for (var i = 0; i < rawWords.Length; i++)
        {
            var wordStart = segStart + wordDuration * i;
            var wordEnd = segStart + wordDuration * (i + 1);
            words.Add(new TranscriptionWord(rawWords[i], wordStart, wordEnd));
        }

        return words;
    }

    private static IReadOnlyList<TranscriptionChunk> GroupIntoChunks(List<TranscriptionWord> words)
    {
        if (words.Count == 0)
        {
            return [];
        }

        var chunks = new List<TranscriptionChunk>();

        for (var i = 0; i < words.Count; i += WordsPerChunk)
        {
            var group = words.Skip(i).Take(WordsPerChunk).ToList();
            if (group.Count == 0)
            {
                continue;
            }

            var chunkText = string.Join(" ", group.Select(w => w.Word));
            var chunkStart = group[0].Start;
            var chunkEnd = group[^1].End;

            // Ensure minimum duration of 200ms
            if (chunkEnd - chunkStart < TimeSpan.FromMilliseconds(200))
            {
                chunkEnd = chunkStart + TimeSpan.FromMilliseconds(500);
            }

            chunks.Add(new TranscriptionChunk(chunkText, chunkStart, chunkEnd));
        }

        return chunks;
    }

    private static string? ResolveFFmpegPath()
    {
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(localPath)) return localPath;

        var currentDir = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe");
        if (File.Exists(currentDir)) return currentDir;

        return "ffmpeg";
    }
}

public sealed record AudioInputForTranscription(
    string Path,
    double StartSeconds,
    double DurationSeconds,
    double SourceStartSeconds);

public sealed record TranscriptionProgress(string Status, double Percent);
