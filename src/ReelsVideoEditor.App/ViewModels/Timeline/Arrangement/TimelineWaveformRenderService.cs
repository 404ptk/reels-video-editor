using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

public sealed class TimelineWaveformRenderService
{
    private const int FfmpegTimeoutMs = 12000;

    public async Task<Bitmap?> TryRenderWaveformAsync(string mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            return null;
        }

        var outputPath = Path.Combine(
            Path.GetTempPath(),
            "ReelsVideoEditor",
            "waveforms",
            $"waveform_{Guid.NewGuid():N}.png");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        foreach (var ffmpegExecutable in GetFfmpegCandidates())
        {
            var generated = await TryGenerateWaveformImageAsync(ffmpegExecutable, mediaPath, outputPath);
            if (!generated)
            {
                continue;
            }

            if (!File.Exists(outputPath))
            {
                continue;
            }

            return new Bitmap(outputPath);
        }

        return null;
    }

    private static async Task<bool> TryGenerateWaveformImageAsync(string ffmpegExecutable, string mediaPath, string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegExecutable,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            processStartInfo.ArgumentList.Add("-hide_banner");
            processStartInfo.ArgumentList.Add("-loglevel");
            processStartInfo.ArgumentList.Add("error");
            processStartInfo.ArgumentList.Add("-y");
            processStartInfo.ArgumentList.Add("-i");
            processStartInfo.ArgumentList.Add(mediaPath);
            processStartInfo.ArgumentList.Add("-filter_complex");
            processStartInfo.ArgumentList.Add("aformat=channel_layouts=stereo,dynaudnorm=f=150:g=7,showwavespic=s=1400x220:split_channels=1:colors=0xC2D8C4:scale=log:draw=full,format=rgba,colorkey=0x000000:0.15:0.0");
            processStartInfo.ArgumentList.Add("-frames:v");
            processStartInfo.ArgumentList.Add("1");
            processStartInfo.ArgumentList.Add(outputPath);

            using var process = new Process { StartInfo = processStartInfo };
            if (!process.Start())
            {
                return false;
            }

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(FfmpegTimeoutMs));
            if (process.ExitCode != 0)
            {
                return false;
            }

            return File.Exists(outputPath);
        }
        catch
        {
            return false;
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

    private static IReadOnlyList<string> DiscoverFfmpegFromWhere()
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "where",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            processStartInfo.ArgumentList.Add("ffmpeg");

            using var process = new Process { StartInfo = processStartInfo };
            if (!process.Start())
            {
                return Array.Empty<string>();
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);

            var lines = output
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return lines;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
