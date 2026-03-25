using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReelsVideoEditor.App.Services.Export;

public class TimelineExportService
{
    public async Task ExportAsync(
        IReadOnlyList<ExportVideoClipInput> videoClips,
        IReadOnlyList<ExportAudioClipInput> audioClips,
        bool isAudioMuted,
        string outputPath,
        string resolution,
        double previewFrameWidth,
        double previewFrameHeight,
        IProgress<double> progress)
    {
        var videos = videoClips
            .OrderByDescending(c => c.LaneOrder)
            .ThenBy(c => c.StartSeconds)
            .ToList();
        var audios = isAudioMuted
            ? new List<ExportAudioClipInput>()
            : audioClips.OrderBy(c => c.StartSeconds).ToList();

        if (videos.Count == 0 && audios.Count == 0)
        {
            throw new InvalidOperationException("No visible or unmuted clips to export.");
        }

        double totalDuration = 0;

        foreach (var v in videos) 
        {
            totalDuration = Math.Max(totalDuration, v.StartSeconds + v.DurationSeconds);
        }
        foreach (var a in audios) 
        {
            totalDuration = Math.Max(totalDuration, a.StartSeconds + a.DurationSeconds);
        }

        if (totalDuration == 0) totalDuration = 5;

        var parts = resolution.Split('x');
        int width = int.Parse(parts[0]);
        int height = int.Parse(parts[1]);

        var ffmpegCommand = new StringBuilder();
        ffmpegCommand.Append("-y ");

        foreach (var v in videos)
        {
            if (IsStillImagePath(v.Path))
            {
                ffmpegCommand.Append("-loop 1 -framerate 30 ");
            }

            ffmpegCommand.Append($"-i \"{v.Path}\" ");
        }
        
        foreach (var a in audios)
        {
            ffmpegCommand.Append($"-i \"{a.Path}\" ");
        }

        ffmpegCommand.Append("-filter_complex \"");

        ffmpegCommand.Append($"color=c=black:s={width}x{height}:d={totalDuration.ToString(CultureInfo.InvariantCulture)}[base];");

        int currentInputIndex = 0;
        var fgOverlayXByIndex = new List<string>(videos.Count);
        var fgOverlayYByIndex = new List<string>(videos.Count);

        for (int i = 0; i < videos.Count; i++)
        {
            var v = videos[i];
            var ptsStart = v.StartSeconds.ToString(CultureInfo.InvariantCulture);

            int extWidth = (int)(width * 1.3);
            extWidth = extWidth % 2 == 0 ? extWidth : extWidth + 1;
            int extHeight = (int)(height * 1.3);
            extHeight = extHeight % 2 == 0 ? extHeight : extHeight + 1;

            var transformScale = Math.Max(0.1, v.TransformScale);
            int scaledWidth = (int)(width * transformScale);
            scaledWidth = scaledWidth % 2 == 0 ? scaledWidth : scaledWidth + 1;
            int scaledHeight = (int)(height * transformScale);
            scaledHeight = scaledHeight % 2 == 0 ? scaledHeight : scaledHeight + 1;

            var safePreviewWidth = Math.Max(1.0, previewFrameWidth);
            var safePreviewHeight = Math.Max(1.0, previewFrameHeight);
            var offsetX = v.TransformX * (width / safePreviewWidth);
            var offsetY = v.TransformY * (height / safePreviewHeight);
            string offsetXStr = offsetX.ToString(CultureInfo.InvariantCulture);
            string offsetYStr = offsetY.ToString(CultureInfo.InvariantCulture);
            fgOverlayXByIndex.Add($"(W-w)/2+{offsetXStr}");
            fgOverlayYByIndex.Add($"(H-h)/2+{offsetYStr}");

            var cropLeft = Math.Clamp(v.CropLeft, 0.0, 0.95);
            var cropTop = Math.Clamp(v.CropTop, 0.0, 0.95);
            var cropRight = Math.Clamp(v.CropRight, 0.0, 0.95);
            var cropBottom = Math.Clamp(v.CropBottom, 0.0, 0.95);
            var visibleCropWidth = Math.Max(0.05, 1.0 - cropLeft - cropRight);
            var visibleCropHeight = Math.Max(0.05, 1.0 - cropTop - cropBottom);
            var cropLeftExpr = cropLeft.ToString(CultureInfo.InvariantCulture);
            var cropTopExpr = cropTop.ToString(CultureInfo.InvariantCulture);
            var cropWidthExpr = visibleCropWidth.ToString(CultureInfo.InvariantCulture);
            var cropHeightExpr = visibleCropHeight.ToString(CultureInfo.InvariantCulture);

            ffmpegCommand.Append($"[{currentInputIndex}:v]split[v{i}_input_bg][v{i}_input_fg];");

            ffmpegCommand.Append($"[v{i}_input_bg]scale={extWidth}:{extHeight}:force_original_aspect_ratio=increase,crop={width}:{height},boxblur=20:5,colorchannelmixer=rr=0.6:gg=0.6:bb=0.6,setpts=PTS-STARTPTS+{ptsStart}/TB[v{i}_bg];");

            ffmpegCommand.Append($"[v{i}_input_fg]crop=iw*{cropWidthExpr}:ih*{cropHeightExpr}:iw*{cropLeftExpr}:ih*{cropTopExpr},scale={scaledWidth}:{scaledHeight}:force_original_aspect_ratio=decrease,setpts=PTS-STARTPTS+{ptsStart}/TB[v{i}_fg];");

            currentInputIndex++;
        }

        string lastBackground = "[base]";
        for (int i = 0; i < videos.Count; i++)
        {
            var v = videos[i];
            var enableStart = v.StartSeconds.ToString(CultureInfo.InvariantCulture);
            var enableEnd = (v.StartSeconds + v.DurationSeconds).ToString(CultureInfo.InvariantCulture);

            var lowerActiveExpression = BuildAnyPreviousLayerActiveExpression(videos, i);
            var activeExpression = $"between(t,{enableStart},{enableEnd})";
            var bgEnableExpression = lowerActiveExpression == "0"
                ? activeExpression
                : $"{activeExpression}*not({lowerActiveExpression})";
            var fgEnableExpression = activeExpression;

            string bgStep = $"[mix{i}_bg]";
            string nextBg = i == videos.Count - 1 ? "[outv]" : $"[bg{i+1}]";
            ffmpegCommand.Append($"{lastBackground}[v{i}_bg]overlay=enable='{bgEnableExpression}':eof_action=pass{bgStep};");
            ffmpegCommand.Append($"{bgStep}[v{i}_fg]overlay={fgOverlayXByIndex[i]}:{fgOverlayYByIndex[i]}:enable='{fgEnableExpression}':eof_action=pass{nextBg};");
            lastBackground = nextBg;
        }

        if (videos.Count == 0)
        {
            ffmpegCommand.Append("[base]copy[outv];");
        }

        var audioOutputs = new List<string>();
        for (int i = 0; i < audios.Count; i++)
        {
            var a = audios[i];
            var delayMs = (int)(a.StartSeconds * 1000);
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
            ffmpegCommand.Append($"amix=inputs={audios.Count}:duration=longest:dropout_transition=2[outa]\"");
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

    private static bool IsStillImagePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAnyPreviousLayerActiveExpression(IReadOnlyList<ExportVideoClipInput> videos, int currentIndex)
    {
        if (currentIndex <= 0)
        {
            return "0";
        }

        var expressions = new List<string>(currentIndex);
        for (var i = 0; i < currentIndex; i++)
        {
            var previous = videos[i];
            var start = previous.StartSeconds.ToString(CultureInfo.InvariantCulture);
            var end = (previous.StartSeconds + previous.DurationSeconds).ToString(CultureInfo.InvariantCulture);
            expressions.Add($"between(t,{start},{end})");
        }

        if (expressions.Count == 0)
        {
            return "0";
        }

        if (expressions.Count == 1)
        {
            return expressions[0];
        }

        return $"gt({string.Join("+", expressions)},0)";
    }

    private static async Task ExecuteFFmpegAsync(string arguments, IProgress<double> progress, double totalDurationSeconds)
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

        var stderrLines = new List<string>();

        while (!process.StandardError.EndOfStream)
        {
            var line = await process.StandardError.ReadLineAsync();
            if (line == null) continue;

            stderrLines.Add(line);
            if (stderrLines.Count > 120)
            {
                stderrLines.RemoveAt(0);
            }

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
            var stderrTail = stderrLines.Count > 0
                ? string.Join(Environment.NewLine, stderrLines.TakeLast(30))
                : "No stderr output captured.";
            throw new Exception($"FFmpeg exited with code {process.ExitCode}.{Environment.NewLine}Arguments: {arguments}{Environment.NewLine}Details:{Environment.NewLine}{stderrTail}");
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

public readonly record struct ExportVideoClipInput(
    string Path,
    double StartSeconds,
    double DurationSeconds,
    int LaneOrder,
    double TransformX,
    double TransformY,
    double TransformScale,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom);

public readonly record struct ExportAudioClipInput(
    string Path,
    double StartSeconds,
    double DurationSeconds,
    double VolumeLevel);
