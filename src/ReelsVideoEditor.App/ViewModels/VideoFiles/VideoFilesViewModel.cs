using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ReelsVideoEditor.App.ViewModels.VideoFiles;

public sealed class VideoFilesViewModel : ViewModelBase
{
    private const int FfmpegTimeoutMs = 12000;
    private const int FfprobeTimeoutMs = 5000;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v"
    };

    public string Title { get; } = "Explorer";

    public string DropHintTitle { get; } = "Drop files here";

    public string DropHintSubtitle { get; } = "Video files will appear in the explorer list";

    public ObservableCollection<VideoFileItem> Files { get; } = [];

    public bool HasFiles => Files.Count > 0;

    public bool NoFiles => !HasFiles;

    public VideoFilesViewModel()
    {
        Files.CollectionChanged += OnFilesChanged;
    }

    public async Task AddDroppedFilesAsync(IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            if (!File.Exists(filePath))
            {
                continue;
            }

            var extension = Path.GetExtension(filePath);
            if (!SupportedExtensions.Contains(extension))
            {
                continue;
            }

            if (ContainsPath(filePath))
            {
                continue;
            }

            var fileItem = new VideoFileItem(Path.GetFileName(filePath), filePath);
            Files.Add(fileItem);

            var durationSeconds = await TryReadDurationSecondsAsync(filePath);
            fileItem.DurationSeconds = durationSeconds > 0 ? durationSeconds : 5;

            var thumbnailResult = await TryCreateThumbnailAsync(filePath);
            var thumbnail = thumbnailResult.Bitmap;

            if (thumbnail is not null)
            {
                fileItem.Thumbnail = thumbnail;
                continue;
            }

            var reason = string.IsNullOrWhiteSpace(thumbnailResult.Error)
                ? "unknown"
                : thumbnailResult.Error;
            fileItem.ThumbnailStatus = $"Thumbnail unavailable ({reason})";
        }
    }

    private static async Task<double> TryReadDurationSecondsAsync(string videoPath)
    {
        foreach (var ffprobeExecutable in GetFfprobeCandidates())
        {
            var (success, duration) = await RunFfprobeAsync(ffprobeExecutable, videoPath);
            if (success && duration > 0)
            {
                return duration;
            }
        }

        return 0;
    }

    private bool ContainsPath(string path)
    {
        foreach (var file in Files)
        {
            if (string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(NoFiles));
    }

    private static async Task<ThumbnailResult> TryCreateThumbnailAsync(string videoPath)
    {
        try
        {
            var thumbnailsDirectory = Path.Combine(Path.GetTempPath(), "ReelsVideoEditor", "thumbnails");
            Directory.CreateDirectory(thumbnailsDirectory);

            var outputPath = Path.Combine(thumbnailsDirectory, $"{Guid.NewGuid():N}.png");

            var attempts = new[]
            {
                new ThumbnailAttempt("thumbnail", null, false, "thumbnail,scale=220:220"),
                new ThumbnailAttempt("seek1", "00:00:01", true, "scale=220:220"),
                new ThumbnailAttempt("seek0", "00:00:00", true, "scale=220:220"),
                new ThumbnailAttempt("noseek", null, false, "scale=220:220")
            };

            string? lastError = null;

            foreach (var attempt in attempts)
            {
                var generationResult = await TryGenerateThumbnailFileAsync(videoPath, outputPath, attempt);
                if (generationResult.Success && File.Exists(outputPath))
                {
                    return new ThumbnailResult(new Bitmap(outputPath), null);
                }

                if (!string.IsNullOrWhiteSpace(generationResult.Error))
                {
                    lastError = generationResult.Error;
                }
            }

            return new ThumbnailResult(null, lastError ?? "ffmpeg failed");
        }
        catch (Exception exception)
        {
            return new ThumbnailResult(null, exception.GetType().Name);
        }
    }

    private static async Task<(bool Success, string? Error)> TryGenerateThumbnailFileAsync(
        string videoPath,
        string outputPath,
        ThumbnailAttempt attempt)
    {
        try
        {
            foreach (var ffmpegExecutable in GetFfmpegCandidates())
            {
                var runResult = await RunFfmpegAsync(ffmpegExecutable, videoPath, outputPath, attempt);
                if (runResult.Success)
                {
                    return (true, null);
                }

                if (!string.IsNullOrWhiteSpace(runResult.Error))
                {
                    return (false, runResult.Error);
                }
            }

            return (false, "ffmpeg not found");
        }
        catch (Exception exception)
        {
            return (false, exception.GetType().Name);
        }
    }

    private static IEnumerable<string> GetFfmpegCandidates()
    {
        var envPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            yield return envPath;
        }

        foreach (var discoveredPath in DiscoverFfmpegFromWhere())
        {
            yield return discoveredPath;
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var commonPaths = new[]
        {
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
            Path.Combine(userHome, "scoop", "shims", "ffmpeg.exe")
        };

        foreach (var path in commonPaths.Where(File.Exists))
        {
            yield return path;
        }

        yield return "ffmpeg.exe";
        yield return "ffmpeg";
    }

    private static IEnumerable<string> GetFfprobeCandidates()
    {
        var envPath = Environment.GetEnvironmentVariable("FFPROBE_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            yield return envPath;
        }

        foreach (var ffmpegCandidate in GetFfmpegCandidates())
        {
            if (ffmpegCandidate.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            {
                var ffprobePath = ffmpegCandidate.Replace("ffmpeg.exe", "ffprobe.exe", StringComparison.OrdinalIgnoreCase);
                if (File.Exists(ffprobePath))
                {
                    yield return ffprobePath;
                }
            }
        }

        yield return "ffprobe.exe";
        yield return "ffprobe";
    }

    private static async Task<(bool Success, double Duration)> RunFfprobeAsync(string ffprobeExecutable, string videoPath)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffprobeExecutable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            processStartInfo.ArgumentList.Add("-v");
            processStartInfo.ArgumentList.Add("error");
            processStartInfo.ArgumentList.Add("-show_entries");
            processStartInfo.ArgumentList.Add("format=duration");
            processStartInfo.ArgumentList.Add("-of");
            processStartInfo.ArgumentList.Add("default=nokey=1:noprint_wrappers=1");
            processStartInfo.ArgumentList.Add(videoPath);

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var completed = await WaitForExitWithTimeoutAsync(process, FfprobeTimeoutMs);
            var output = await outputTask;

            if (!completed || process.ExitCode != 0)
            {
                return (false, 0);
            }

            var trimmed = output.Trim();
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) && duration > 0)
            {
                return (true, duration);
            }

            return (false, 0);
        }
        catch
        {
            return (false, 0);
        }
    }

    private static IEnumerable<string> DiscoverFfmpegFromWhere()
    {
        var discovered = new List<string>();

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c where ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return discovered;
            }

            var lines = output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                discovered.Add(line);
            }
        }
        catch
        {
            return discovered;
        }

        return discovered;
    }

    private static async Task<(bool Success, string? Error)> RunFfmpegAsync(
        string ffmpegExecutable,
        string videoPath,
        string outputPath,
        ThumbnailAttempt attempt)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegExecutable,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            processStartInfo.ArgumentList.Add("-y");

            if (attempt.SeekBeforeInput && !string.IsNullOrWhiteSpace(attempt.Seek))
            {
                processStartInfo.ArgumentList.Add("-ss");
                processStartInfo.ArgumentList.Add(attempt.Seek);
            }

            processStartInfo.ArgumentList.Add("-i");
            processStartInfo.ArgumentList.Add(videoPath);

            if (!attempt.SeekBeforeInput && !string.IsNullOrWhiteSpace(attempt.Seek))
            {
                processStartInfo.ArgumentList.Add("-ss");
                processStartInfo.ArgumentList.Add(attempt.Seek);
            }

            processStartInfo.ArgumentList.Add("-an");
            processStartInfo.ArgumentList.Add("-frames:v");
            processStartInfo.ArgumentList.Add("1");
            processStartInfo.ArgumentList.Add("-vf");
            processStartInfo.ArgumentList.Add(attempt.Filter);
            processStartInfo.ArgumentList.Add(outputPath);

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();
            var completed = await WaitForExitWithTimeoutAsync(process);

            if (!completed)
            {
                return (false, "timeout");
            }

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                return (true, null);
            }

            return (false, $"{attempt.Name}: exit {process.ExitCode}");
        }
        catch
        {
            return await TryRunFfmpegViaCmdAsync(ffmpegExecutable, videoPath, outputPath, attempt);
        }
    }

    private static async Task<(bool Success, string? Error)> TryRunFfmpegViaCmdAsync(
        string ffmpegExecutable,
        string videoPath,
        string outputPath,
        ThumbnailAttempt attempt)
    {
        try
        {
            var seekPartBefore = attempt.SeekBeforeInput && !string.IsNullOrWhiteSpace(attempt.Seek) ? $"-ss {attempt.Seek} " : string.Empty;
            var seekPartAfter = !attempt.SeekBeforeInput && !string.IsNullOrWhiteSpace(attempt.Seek) ? $" -ss {attempt.Seek}" : string.Empty;

            var command =
                $"\"{ffmpegExecutable}\" -y {seekPartBefore}-i \"{videoPath}\"{seekPartAfter} -an -frames:v 1 -vf \"{attempt.Filter}\" \"{outputPath}\"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();
            var completed = await WaitForExitWithTimeoutAsync(process);

            if (!completed)
            {
                return (false, "cmd-timeout");
            }

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                return (true, null);
            }

            return (false, $"cmd-{attempt.Name}: exit {process.ExitCode}");
        }
        catch
        {
            return (false, $"cmd-{attempt.Name}: exception");
        }
    }

    private static async Task<bool> WaitForExitWithTimeoutAsync(Process process)
    {
        return await WaitForExitWithTimeoutAsync(process, FfmpegTimeoutMs);
    }

    private static async Task<bool> WaitForExitWithTimeoutAsync(Process process, int timeoutMs)
    {
        var exitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(timeoutMs);

        var completedTask = await Task.WhenAny(exitTask, timeoutTask);
        if (completedTask == exitTask)
        {
            return true;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        return false;
    }

}

public sealed record ThumbnailResult(Bitmap? Bitmap, string? Error);

public sealed record ThumbnailAttempt(string Name, string? Seek, bool SeekBeforeInput, string Filter);

public sealed partial class VideoFileItem : ObservableObject
{
    public string Name { get; }

    public string Path { get; }

    [ObservableProperty]
    private Bitmap? thumbnail;

    [ObservableProperty]
    private string thumbnailStatus = "Generating thumbnail...";

    [ObservableProperty]
    private double durationSeconds = 5;

    public bool HasThumbnail => Thumbnail is not null;

    public VideoFileItem(string name, string path)
    {
        Name = name;
        Path = path;
    }

    partial void OnThumbnailChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasThumbnail));
    }
}
