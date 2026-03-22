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
        double renderOffsetX,
        double renderOffsetY,
        double renderScale,
        double cropLeft,
        double cropTop,
        double cropRight,
        double cropBottom,
        string outputPath,
        string resolution,
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

        var parts = resolution.Split('x');
        int width = int.Parse(parts[0]);
        int height = int.Parse(parts[1]);

        var ffmpegCommand = new StringBuilder();
        ffmpegCommand.Append("-y ");

        cropLeft = Math.Clamp(cropLeft, 0.0, 0.95);
        cropTop = Math.Clamp(cropTop, 0.0, 0.95);
        cropRight = Math.Clamp(cropRight, 0.0, 0.95);
        cropBottom = Math.Clamp(cropBottom, 0.0, 0.95);

        var visibleCropWidth = Math.Max(0.05, 1.0 - cropLeft - cropRight);
        var visibleCropHeight = Math.Max(0.05, 1.0 - cropTop - cropBottom);
        var cropLeftExpr = cropLeft.ToString(CultureInfo.InvariantCulture);
        var cropTopExpr = cropTop.ToString(CultureInfo.InvariantCulture);
        var cropWidthExpr = visibleCropWidth.ToString(CultureInfo.InvariantCulture);
        var cropHeightExpr = visibleCropHeight.ToString(CultureInfo.InvariantCulture);

        foreach (var v in videos)
        {
            ffmpegCommand.Append($"-i \"{v.Path}\" ");
        }
        
        foreach (var a in audios)
        {
            ffmpegCommand.Append($"-i \"{a.Path}\" ");
        }

        ffmpegCommand.Append("-filter_complex \"");

        ffmpegCommand.Append($"color=c=black:s={width}x{height}:d={totalDuration.ToString(CultureInfo.InvariantCulture)}[base];");

        int currentInputIndex = 0;

        var videoOutputs = new List<string>();
        for (int i = 0; i < videos.Count; i++)
        {
            var v = videos[i];
            var adjustedStart = v.StartSeconds - timeOffset;
            var ptsStart = adjustedStart.ToString(CultureInfo.InvariantCulture);

            int extWidth = (int)(width * 1.3);
            extWidth = extWidth % 2 == 0 ? extWidth : extWidth + 1;
            int extHeight = (int)(height * 1.3);
            extHeight = extHeight % 2 == 0 ? extHeight : extHeight + 1;

            int scaledWidth = (int)(width * renderScale);
            scaledWidth = scaledWidth % 2 == 0 ? scaledWidth : scaledWidth + 1;
            int scaledHeight = (int)(height * renderScale);
            scaledHeight = scaledHeight % 2 == 0 ? scaledHeight : scaledHeight + 1;

            string offsetXStr = renderOffsetX.ToString(CultureInfo.InvariantCulture);
            string offsetYStr = renderOffsetY.ToString(CultureInfo.InvariantCulture);

            ffmpegCommand.Append($"[{currentInputIndex}:v]split[v{i}_input_bg][v{i}_input_fg];");

            ffmpegCommand.Append($"[v{i}_input_bg]scale={extWidth}:{extHeight}:force_original_aspect_ratio=increase,crop={width}:{height},boxblur=20:5,colorchannelmixer=rr=0.6:gg=0.6:bb=0.6,setpts=PTS-STARTPTS+{ptsStart}/TB[v{i}_bg];");

            ffmpegCommand.Append($"[v{i}_input_fg]crop=iw*{cropWidthExpr}:ih*{cropHeightExpr}:iw*{cropLeftExpr}:ih*{cropTopExpr},scale={scaledWidth}:{scaledHeight}:force_original_aspect_ratio=decrease,pad={scaledWidth}:{scaledHeight}:x=iw*{cropLeftExpr}:y=ih*{cropTopExpr}:color=black,setpts=PTS-STARTPTS+{ptsStart}/TB[v{i}_fg];");

            ffmpegCommand.Append($"[v{i}_bg][v{i}_fg]overlay=(W-w)/2+{offsetXStr}:(H-h)/2+{offsetYStr}[v{i}];");

            videoOutputs.Add($"[v{i}]");
            currentInputIndex++;
        }

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

        var audioOutputs = new List<string>();
        for (int i = 0; i < audios.Count; i++)
        {
            var a = audios[i];
            var adjustedStart = a.StartSeconds - timeOffset;
            var delayMs = (int)(adjustedStart * 1000);
            var volume = Math.Clamp(a.VolumeLevel, 0.0, 1.0).ToString(CultureInfo.InvariantCulture);

            ffmpegCommand.Append($"[{currentInputIndex}:a]volume={volume},adelay={delayMs}|{delayMs}[a{i}];");
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
            ffmpegCommand.Append("anullsrc=r=48000:cl=stereo[outa]\"");
        }

        ffmpegCommand.Append(" -map \"[outv]\" -map \"[outa]\" ");
        
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
                RedirectStandardError = true,
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

            if (line.Contains("time="))
            {
                var timeStr = line.Split("time=")[1].Split(" ")[0];
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
        
        var currentDir = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe");
        if (File.Exists(currentDir)) return currentDir;
        
        return "ffmpeg";
    }
}
