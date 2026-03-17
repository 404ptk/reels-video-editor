using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v"
    };

    public string Title { get; } = "Explorer";

    public string DropHintTitle { get; } = "Drop files here";

    public string DropHintSubtitle { get; } = "Video files will appear in the explorer list";

    public ObservableCollection<VideoFileItem> Files { get; } = [];

    public bool HasFiles => Files.Count > 0;

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
        var exitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(FfmpegTimeoutMs);

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
