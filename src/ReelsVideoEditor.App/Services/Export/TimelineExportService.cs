using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ReelsVideoEditor.App.Services.Compositor;
using ReelsVideoEditor.App.Services.Text;
using ReelsVideoEditor.App.Services.VideoDecoder;
using ReelsVideoEditor.App.ViewModels.Timeline;
using SkiaSharp;

namespace ReelsVideoEditor.App.Services.Export;

public class TimelineExportService
{
    private const int AccurateExportFps = 30;

    private sealed class AccurateExportDecoderState
    {
        public required VideoFrameDecoder Decoder { get; init; }

        public long LastRequestedMs { get; set; } = -1;

        public long LastSourceFrameIndex { get; set; } = -1;
    }

    private readonly record struct QuantizedFrameRequest(long TimestampMs, long FrameIndex, double FrameStepMs);

    public async Task ExportAccurateAsync(
        IReadOnlyList<ExportAudioClipInput> audioClips,
        bool isAudioMuted,
        string outputPath,
        string resolution,
        double previewFrameWidth,
        double previewFrameHeight,
        long playbackDurationMilliseconds,
        Func<long, IReadOnlyList<PreviewVideoLayer>> resolveVideoLayers,
        Func<long, TimelineTextOverlayState> resolveTextOverlayState,
        IProgress<double> progress)
    {
        var parts = resolution.Split('x');
        var width = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var height = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var totalDurationMs = Math.Max(1, playbackDurationMilliseconds);
        var totalDurationSeconds = totalDurationMs / 1000.0;
        var totalFrames = Math.Max(1, (int)Math.Ceiling(totalDurationSeconds * AccurateExportFps));

        var tempDir = Path.Combine(Path.GetTempPath(), "ReelsVideoEditor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempVideoPath = Path.Combine(tempDir, "accurate_video.mp4");
        var tempAudioPath = Path.Combine(tempDir, "mixed_audio.m4a");

        try
        {
            await RenderAccurateVideoAsync(
                tempVideoPath,
                width,
                height,
                previewFrameWidth,
                previewFrameHeight,
                totalFrames,
                resolveVideoLayers,
                resolveTextOverlayState,
                progress);

            progress.Report(88);

            var hasAudio = !isAudioMuted && audioClips.Count > 0;
            if (hasAudio)
            {
                progress.Report(90);
                await RenderMixedAudioAsync(audioClips, tempAudioPath, totalDurationSeconds);
                progress.Report(95);
            }

            progress.Report(97);
            await MuxAccurateOutputAsync(tempVideoPath, hasAudio ? tempAudioPath : null, outputPath, totalDurationSeconds);
            progress.Report(100);
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

        var maxVideo = videos.Count > 0 ? videos.Max(v => v.StartSeconds + v.DurationSeconds) : 0;
        var maxAudio = audios.Count > 0 ? audios.Max(a => a.StartSeconds + a.DurationSeconds) : 0;
        var totalDuration = Math.Max(maxVideo, maxAudio);

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
            var sourceStart = Math.Max(0, v.SourceStartSeconds).ToString(CultureInfo.InvariantCulture);
            var sourceDuration = Math.Max(0.001, v.DurationSeconds).ToString(CultureInfo.InvariantCulture);

            int extWidth = (int)(width * 1.3);
            extWidth += extWidth % 2;
            int extHeight = (int)(height * 1.3);
            extHeight += extHeight % 2;

            var transformScale = Math.Max(0.1, v.TransformScale);
            int scaledWidth = (int)(width * transformScale);
            scaledWidth += scaledWidth % 2;
            int scaledHeight = (int)(height * transformScale);
            scaledHeight += scaledHeight % 2;

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
            if (IsStillImagePath(v.Path))
            {
                ffmpegCommand.Append($"[v{i}_input_bg]scale={extWidth}:{extHeight}:force_original_aspect_ratio=increase,crop={width}:{height},boxblur=20:5,colorchannelmixer=rr=0.6:gg=0.6:bb=0.6,setpts=PTS-STARTPTS+{ptsStart}/TB[v{i}_bg];");
                ffmpegCommand.Append($"[v{i}_input_fg]crop=iw*{cropWidthExpr}:ih*{cropHeightExpr}:iw*{cropLeftExpr}:ih*{cropTopExpr},scale={scaledWidth}:{scaledHeight}:force_original_aspect_ratio=decrease,setpts=PTS-STARTPTS+{ptsStart}/TB[v{i}_fg];");
            }
            else
            {
                ffmpegCommand.Append($"[v{i}_input_bg]trim=start={sourceStart}:duration={sourceDuration},setpts=PTS-STARTPTS,scale={extWidth}:{extHeight}:force_original_aspect_ratio=increase,crop={width}:{height},boxblur=20:5,colorchannelmixer=rr=0.6:gg=0.6:bb=0.6,setpts=PTS-STARTPTS+{ptsStart}/TB[v{i}_bg];");
                ffmpegCommand.Append($"[v{i}_input_fg]trim=start={sourceStart}:duration={sourceDuration},setpts=PTS-STARTPTS,crop=iw*{cropWidthExpr}:ih*{cropHeightExpr}:iw*{cropLeftExpr}:ih*{cropTopExpr},scale={scaledWidth}:{scaledHeight}:force_original_aspect_ratio=decrease,setpts=PTS-STARTPTS+{ptsStart}/TB[v{i}_fg];");
            }

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
            var sourceStart = Math.Max(0, a.SourceStartSeconds).ToString(CultureInfo.InvariantCulture);
            var sourceDuration = Math.Max(0.001, a.DurationSeconds).ToString(CultureInfo.InvariantCulture);
            var volume = Math.Clamp(a.VolumeLevel, 0.0, 1.0).ToString(CultureInfo.InvariantCulture);

            ffmpegCommand.Append($"[{currentInputIndex}:a]atrim=start={sourceStart}:duration={sourceDuration},asetpts=PTS-STARTPTS,volume={volume},adelay={delayMs}|{delayMs}[a{i}];");
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

    private async Task RenderAccurateVideoAsync(
        string tempVideoPath,
        int width,
        int height,
        double previewFrameWidth,
        double previewFrameHeight,
        int totalFrames,
        Func<long, IReadOnlyList<PreviewVideoLayer>> resolveVideoLayers,
        Func<long, TimelineTextOverlayState> resolveTextOverlayState,
        IProgress<double> progress)
    {
        var ffmpegPath = ResolveFFmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new FileNotFoundException("Could not find ffmpeg.exe. Ensure FFmpeg is accessible in the PATH or bundled with the App.");
        }

        var args = string.Join(' ',
            "-y",
            "-f rawvideo",
            "-pix_fmt bgra",
            $"-s {width}x{height}",
            $"-r {AccurateExportFps.ToString(CultureInfo.InvariantCulture)}",
            "-i -",
            "-an",
            "-c:v libx264",
            "-preset fast",
            "-crf 20",
            "-pix_fmt yuv420p",
            "-movflags +faststart",
            $"\"{tempVideoPath}\"");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stderrLines = new List<string>();
        var stderrDrain = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line is null)
                {
                    continue;
                }

                lock (stderrLines)
                {
                    stderrLines.Add(line);
                    if (stderrLines.Count > 200)
                    {
                        stderrLines.RemoveAt(0);
                    }
                }
            }
        });
        var stdoutDrain = process.StandardOutput.ReadToEndAsync();

        var decoderByPath = new Dictionary<string, AccurateExportDecoderState>(StringComparer.OrdinalIgnoreCase);
        using var compositor = new FrameCompositor();
        byte[]? frameBuffer = null;
        var safePreviewWidth = Math.Max(1.0, previewFrameWidth);
        var safePreviewHeight = Math.Max(1.0, previewFrameHeight);

        try
        {
            for (var frameIndex = 0; frameIndex < totalFrames; frameIndex++)
            {
                var playbackMs = (long)Math.Round(frameIndex * (1000.0 / AccurateExportFps), MidpointRounding.AwayFromZero);
                var layers = resolveVideoLayers(playbackMs);

                var frameLayers = new List<FrameCompositor.FrameLayer>(layers.Count);
                for (var i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    if (string.IsNullOrWhiteSpace(layer.Path))
                    {
                        continue;
                    }

                    if (!decoderByPath.TryGetValue(layer.Path, out var decoderState))
                    {
                        var openedDecoder = new VideoFrameDecoder();
                        openedDecoder.Open(layer.Path);
                        decoderState = new AccurateExportDecoderState
                        {
                            Decoder = openedDecoder
                        };
                        decoderByPath[layer.Path] = decoderState;
                    }

                    var decoder = decoderState.Decoder;

                    var quantizedRequest = QuantizeToSourceFrameRequest(
                        Math.Max(0, layer.PlaybackMilliseconds),
                        decoder.FrameRate,
                        decoder.Duration);
                    var quantizedLayerPlaybackMs = quantizedRequest.TimestampMs;

                    byte[]? frameBytes;
                    if (decoderState.LastSourceFrameIndex == quantizedRequest.FrameIndex)
                    {
                        frameBytes = decoder.GetCurrentFrame()
                            ?? decoder.SeekAndRead(TimeSpan.FromMilliseconds(quantizedLayerPlaybackMs));
                    }
                    else
                    {
                        var forwardFrameDelta = quantizedRequest.FrameIndex - decoderState.LastSourceFrameIndex;
                        var canReadSequentially = decoderState.LastSourceFrameIndex >= 0
                            && forwardFrameDelta > 0
                            && forwardFrameDelta <= 8;

                        if (canReadSequentially)
                        {
                            frameBytes = null;
                            for (var step = 0L; step < forwardFrameDelta; step++)
                            {
                                var stepFrameIndex = decoderState.LastSourceFrameIndex + step + 1;
                                var stepTimestampMs = (long)Math.Round(
                                    stepFrameIndex * quantizedRequest.FrameStepMs,
                                    MidpointRounding.AwayFromZero);
                                frameBytes = decoder.ReadNextFrame(TimeSpan.FromMilliseconds(stepTimestampMs));
                            }

                            if (frameBytes is null)
                            {
                                frameBytes = decoder.SeekAndRead(TimeSpan.FromMilliseconds(quantizedLayerPlaybackMs));
                            }
                        }
                        else
                        {
                            frameBytes = decoder.SeekAndRead(TimeSpan.FromMilliseconds(quantizedLayerPlaybackMs));
                        }
                    }

                    if (frameBytes is null)
                    {
                        continue;
                    }

                    decoderState.LastRequestedMs = quantizedLayerPlaybackMs;
                    decoderState.LastSourceFrameIndex = quantizedRequest.FrameIndex;

                    var renderOffsetX = (float)(layer.TransformX * (width / safePreviewWidth));
                    var renderOffsetY = (float)(layer.TransformY * (height / safePreviewHeight));
                    frameLayers.Add(new FrameCompositor.FrameLayer(
                        frameBytes,
                        decoder.FrameWidth,
                        decoder.FrameHeight,
                        renderOffsetX,
                        renderOffsetY,
                        (float)layer.TransformScale,
                        (float)layer.CropLeft,
                        (float)layer.CropTop,
                        (float)layer.CropRight,
                        (float)layer.CropBottom,
                        layer.DrawBlurredBackground,
                        (float)layer.Opacity));
                }

                var composed = compositor.ComposeLayers(frameLayers, width, height);
                TimelineTextOverlayRenderer.Draw(
                    composed,
                    resolveTextOverlayState(playbackMs),
                    height,
                    safePreviewWidth,
                    safePreviewHeight);

                var byteCount = width * height * 4;
                if (frameBuffer is null || frameBuffer.Length != byteCount)
                {
                    frameBuffer = new byte[byteCount];
                }

                Marshal.Copy(composed.GetPixels(), frameBuffer, 0, byteCount);
                await process.StandardInput.BaseStream.WriteAsync(frameBuffer, 0, frameBuffer.Length);

                var pct = ((frameIndex + 1) / (double)totalFrames) * 85.0;
                progress.Report(pct);
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync();
            await Task.WhenAll(stderrDrain, stdoutDrain);
            if (process.ExitCode != 0)
            {
                var stderrTail = stderrLines.Count > 0
                    ? string.Join(Environment.NewLine, stderrLines.TakeLast(40))
                    : "No stderr output captured.";
                throw new Exception($"Accurate video encoding failed with ffmpeg exit code {process.ExitCode}.\n{stderrTail}");
            }
        }
        finally
        {
            foreach (var decoderState in decoderByPath.Values)
            {
                decoderState.Decoder.Dispose();
            }
        }
    }

    private static double ResolveFrameStepMilliseconds(double sourceFps)
    {
        var safeFps = sourceFps > 0.001 ? sourceFps : 30.0;
        return 1000.0 / safeFps;
    }

    private static QuantizedFrameRequest QuantizeToSourceFrameRequest(long requestedMs, double sourceFps, TimeSpan sourceDuration)
    {
        if (requestedMs <= 0)
        {
            return new QuantizedFrameRequest(0, 0, ResolveFrameStepMilliseconds(sourceFps));
        }

        var frameStepMs = ResolveFrameStepMilliseconds(sourceFps);
        if (frameStepMs <= 0.001)
        {
            return new QuantizedFrameRequest(requestedMs, requestedMs, frameStepMs);
        }

        var maxSafeMs = Math.Max(0.0, sourceDuration.TotalMilliseconds - frameStepMs);
        var clampedMs = Math.Min(requestedMs, maxSafeMs);
        var frameIndex = (long)Math.Max(0, Math.Round(clampedMs / frameStepMs, MidpointRounding.AwayFromZero));
        var quantizedMs = frameIndex * frameStepMs;
        if (quantizedMs > maxSafeMs)
        {
            quantizedMs = maxSafeMs;
        }

        return new QuantizedFrameRequest(
            (long)Math.Max(0, Math.Round(quantizedMs, MidpointRounding.AwayFromZero)),
            frameIndex,
            frameStepMs);
    }

    private async Task RenderMixedAudioAsync(IReadOnlyList<ExportAudioClipInput> audios, string outputAudioPath, double totalDurationSeconds)
    {
        var ffmpeg = new StringBuilder();
        ffmpeg.Append("-y ");
        foreach (var a in audios)
        {
            ffmpeg.Append($"-i \"{a.Path}\" ");
        }

        ffmpeg.Append("-filter_complex \"");
        for (var i = 0; i < audios.Count; i++)
        {
            var a = audios[i];
            var delayMs = (int)Math.Round(a.StartSeconds * 1000, MidpointRounding.AwayFromZero);
            var sourceStart = Math.Max(0, a.SourceStartSeconds).ToString(CultureInfo.InvariantCulture);
            var sourceDuration = Math.Max(0.001, a.DurationSeconds).ToString(CultureInfo.InvariantCulture);
            var volume = Math.Clamp(a.VolumeLevel, 0.0, 1.0).ToString(CultureInfo.InvariantCulture);
            ffmpeg.Append($"[{i}:a]atrim=start={sourceStart}:duration={sourceDuration},asetpts=PTS-STARTPTS,volume={volume},adelay={delayMs}|{delayMs}[a{i}];");
        }

        for (var i = 0; i < audios.Count; i++)
        {
            ffmpeg.Append($"[a{i}]");
        }

        ffmpeg.Append($"amix=inputs={audios.Count}:duration=longest:dropout_transition=2[outa]\" ");
        ffmpeg.Append("-map \"[outa]\" -c:a aac -b:a 192k ");
        ffmpeg.Append($"-t {totalDurationSeconds.ToString(CultureInfo.InvariantCulture)} ");
        ffmpeg.Append($"\"{outputAudioPath}\"");

        await ExecuteFFmpegAsync(ffmpeg.ToString(), new Progress<double>(), totalDurationSeconds);
    }

    private async Task MuxAccurateOutputAsync(string videoPath, string? audioPath, string outputPath, double totalDurationSeconds)
    {
        var ffmpeg = new StringBuilder();
        ffmpeg.Append("-y ");
        ffmpeg.Append($"-i \"{videoPath}\" ");

        if (!string.IsNullOrWhiteSpace(audioPath) && File.Exists(audioPath))
        {
            ffmpeg.Append($"-i \"{audioPath}\" -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -movflags +faststart -shortest ");
        }
        else
        {
            ffmpeg.Append("-map 0:v:0 -c copy -movflags +faststart -an ");
        }

        ffmpeg.Append($"-t {totalDurationSeconds.ToString(CultureInfo.InvariantCulture)} ");
        ffmpeg.Append($"\"{outputPath}\"");

        await ExecuteFFmpegAsync(ffmpeg.ToString(), new Progress<double>(), totalDurationSeconds);
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
    double SourceStartSeconds,
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
    double SourceStartSeconds,
    double VolumeLevel);
