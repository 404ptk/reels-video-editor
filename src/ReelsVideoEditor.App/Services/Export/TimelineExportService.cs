using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.Services.Export;

public class TimelineExportService
{
    public async Task ExportAsync(
        IEnumerable<TimelineClipItem> videoClips,
        IEnumerable<TimelineClipItem> audioClips,
        bool isVideoHidden,
        bool isAudioMuted,
        string outputPath,
        string resolution, // e.g. "1080x1920"
        IProgress<double> progress)
    {
        var videos = isVideoHidden ? new List<TimelineClipItem>() : videoClips.OrderBy(c => c.StartSeconds).ToList();
        var audios = isAudioMuted ? new List<TimelineClipItem>() : audioClips.OrderBy(c => c.StartSeconds).ToList();

        if (videos.Count == 0 && audios.Count == 0)
        {
            throw new InvalidOperationException("No visible or unmuted clips to export.");
        }

        double totalDuration = 0;
        double minStartSeconds = double.MaxValue;

        foreach (var v in videos) 
        {
            totalDuration = Math.Max(totalDuration, v.StartSeconds + v.DurationSeconds);
            minStartSeconds = Math.Min(minStartSeconds, v.StartSeconds);
        }
        foreach (var a in audios) 
        {
            totalDuration = Math.Max(totalDuration, a.StartSeconds + a.DurationSeconds);
            minStartSeconds = Math.Min(minStartSeconds, a.StartSeconds);
        }

        if (totalDuration == 0) totalDuration = 5;
        
        double timeOffset = 0;
        if (minStartSeconds > 0 && minStartSeconds != double.MaxValue)
        {
            timeOffset = minStartSeconds;
            totalDuration -= timeOffset;
        }

        // Parse resolution
        var parts = resolution.Split('x');
        int width = int.Parse(parts[0]);
        int height = int.Parse(parts[1]);

        var ffmpegCommand = new StringBuilder();
        ffmpegCommand.Append("-y "); // Overwrite output

        // Inputs
        foreach (var v in videos)
        {
            ffmpegCommand.Append($"-i \"{v.Path}\" ");
        }
        
        foreach (var a in audios)
        {
            ffmpegCommand.Append($"-i \"{a.Path}\" ");
        }

        // Filter complex
        ffmpegCommand.Append("-filter_complex \"");

        // 1. Black background base
        ffmpegCommand.Append($"color=c=black:s={width}x{height}:d={totalDuration.ToString(CultureInfo.InvariantCulture)}[base];");

        int currentInputIndex = 0;

        // 2. Video streams scaling and positioning
        var videoOutputs = new List<string>();
        for (int i = 0; i < videos.Count; i++)
        {
            var v = videos[i];
            var adjustedStart = v.StartSeconds - timeOffset;
            var ptsStart = adjustedStart.ToString(CultureInfo.InvariantCulture);

            // Format video: scale to fit, pad if necessary, delay using setpts
            ffmpegCommand.Append($"[{currentInputIndex}:v]scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setpts=PTS-STARTPTS+{ptsStart}/TB[v{i}];");
            videoOutputs.Add($"[v{i}]");
            currentInputIndex++;
        }

        // Overlay videos on base
        string lastBackground = "[base]";
        for (int i = 0; i < videos.Count; i++)
        {
            var v = videos[i];
            var adjustedStart = v.StartSeconds - timeOffset;
            var enableStart = adjustedStart.ToString(CultureInfo.InvariantCulture);
            var enableEnd = (adjustedStart + v.DurationSeconds).ToString(CultureInfo.InvariantCulture);

            string nextBg = i == videos.Count - 1 ? "[outv]" : $"[bg{i+1}]";
            ffmpegCommand.Append($"{lastBackground}[v{i}]overlay=enable='between(t,{enableStart},{enableEnd})':eof_action=pass{nextBg};");
            lastBackground = nextBg;
        }        if (videos.Count == 0)
        {
            ffmpegCommand.Append("[base]copy[outv];");
        }

        // 3. Audio streams delaying
        var audioOutputs = new List<string>();
        for (int i = 0; i < audios.Count; i++)
        {
            var a = audios[i];
            var adjustedStart = a.StartSeconds - timeOffset;
            var delayMs = (int)(adjustedStart * 1000);

            ffmpegCommand.Append($"[{currentInputIndex}:a]adelay={delayMs}|{delayMs}[a{i}];");
            audioOutputs.Add($"[a{i}]");
            currentInputIndex++;
        }        if (audios.Count > 0)
        {
            foreach (var aOut in audioOutputs)
            {
                ffmpegCommand.Append(aOut);
            }
            ffmpegCommand.Append($"amix=inputs={audios.Count}:duration=first:dropout_transition=2[outa]\"");
        }
        else
        {
            // If no audio, just add an empty audio track or remove complex map for audio
            ffmpegCommand.Append("anullsrc=r=48000:cl=stereo[outa]\"");
        }

        ffmpegCommand.Append(" -map \"[outv]\" -map \"[outa]\" ");
        
        // Codecs
        ffmpegCommand.Append($"-c:v libx264 -preset fast -crf 22 -c:a aac -b:a 192k -t {totalDuration.ToString(CultureInfo.InvariantCulture)} ");
        
        ffmpegCommand.Append($"\"{outputPath}\"");

        await ExecuteFFmpegAsync(ffmpegCommand.ToString(), progress, totalDuration);
    }

    private async Task ExecuteFFmpegAsync(string arguments, IProgress<double> progress, double totalDurationSeconds)
    {
        var ffmpegPath = ResolveFFmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new FileNotFoundException("Could not find ffmpeg.exe. Ensure FFmpeg is accessible in the PATH or bundled with the App.");
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true, // FFmpeg outputs progress to stderr
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        while (!process.StandardError.EndOfStream)
        {
            var line = await process.StandardError.ReadLineAsync();
            if (line == null) continue;

            // Example line: "frame=  222 fps= 34 q=28.0 size=     512kB time=00:00:07.41 bitrate= 565.4kbits/s speed=1.15x    "
            if (line.Contains("time="))
            {
                var timeStr = line.Split("time=")[1].Split(" ")[0]; // "00:00:07.41"
                if (TimeSpan.TryParse(timeStr, out var time))
                {
                    double percent = (time.TotalSeconds / totalDurationSeconds) * 100;
                    progress.Report(Math.Clamp(percent, 0, 100));
                }
            }
        }

        await process.WaitForExitAsync();
        progress.Report(100);

        if (process.ExitCode != 0)
        {
            throw new Exception($"FFmpeg exited with code {process.ExitCode}");
        }
    }

    private static string? ResolveFFmpegPath()
    {
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(localPath)) return localPath;
        
        // Search in current directory
        var currentDir = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe");
        if (File.Exists(currentDir)) return currentDir;
        
        // Fallback to systemic ffmpeg
        return "ffmpeg";
    }
}