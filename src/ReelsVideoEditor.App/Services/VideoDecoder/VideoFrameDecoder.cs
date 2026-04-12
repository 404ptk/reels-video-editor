using System;
using System.IO;
using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;

namespace ReelsVideoEditor.App.Services.VideoDecoder;

public sealed class VideoFrameDecoder : IDisposable
{
    private static bool isFFmpegInitialized;

    private MediaFile? mediaFile;
    private byte[]? lastFrameBuffer;
    private TimeSpan lastDecodedPosition = TimeSpan.FromSeconds(-1);
    private bool isSequentialMode;
    private bool disposed;

    public int FrameWidth { get; private set; }

    public int FrameHeight { get; private set; }

    public double FrameRate { get; private set; }

    public TimeSpan Duration { get; private set; }

    public bool IsOpen => mediaFile is not null;

    public static void InitializeFFmpeg()
    {
        if (isFFmpegInitialized)
        {
            return;
        }

        FFmpegLoader.FFmpegPath = AppDomain.CurrentDomain.BaseDirectory;
        isFFmpegInitialized = true;
    }

    public void Open(string path)
    {
        Close();

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Video file not found.", path);
        }

        InitializeFFmpeg();

        var options = new MediaOptions
        {
            VideoPixelFormat = ImagePixelFormat.Bgra32
        };

        mediaFile = MediaFile.Open(path, options);

        var videoInfo = mediaFile.Video.Info;
        FrameWidth = videoInfo.FrameSize.Width;
        FrameHeight = videoInfo.FrameSize.Height;
        Duration = videoInfo.Duration;
        FrameRate = videoInfo.AvgFrameRate > 0 ? videoInfo.AvgFrameRate : 30.0;

        lastFrameBuffer = null;
        lastDecodedPosition = TimeSpan.FromSeconds(-1);
        isSequentialMode = false;
    }

    /// <summary>
    /// Seeks to an absolute position. Use this for scrubbing/seeking.
    /// After calling this, subsequent ReadNextFrame() calls will continue sequentially from here.
    /// </summary>
    public byte[]? SeekAndRead(TimeSpan position)
    {
        if (mediaFile is null)
        {
            return null;
        }

        var clampedPosition = ClampPosition(position);

        if (clampedPosition == lastDecodedPosition && lastFrameBuffer is not null)
        {
            return lastFrameBuffer;
        }

        isSequentialMode = false;

        try
        {
            var frame = mediaFile.Video.GetFrame(clampedPosition);
            CopyFrameToBuffer(frame);
            lastDecodedPosition = clampedPosition;
            isSequentialMode = true;
            return lastFrameBuffer;
        }
        catch (Exception ex)
        {
            File.AppendAllText("decoder_log.txt", $"SeekAndRead Error at {clampedPosition}: {ex.Message}\n{ex.StackTrace}\n");
            return lastFrameBuffer;
        }
    }

    /// <summary>
    /// Reads the next sequential frame. Use this during playback for smooth frame advancement.
    /// Falls back to SeekAndRead if sequential mode hasn't been established yet.
    /// </summary>
    public byte[]? ReadNextFrame(TimeSpan expectedPosition)
    {
        if (mediaFile is null)
        {
            return null;
        }

        if (!isSequentialMode)
        {
            return SeekAndRead(expectedPosition);
        }

        try
        {
            if (mediaFile.Video.TryGetNextFrame(out var frame))
            {
                CopyFrameToBuffer(frame);
                lastDecodedPosition = expectedPosition;
                return lastFrameBuffer;
            }

            File.AppendAllText("decoder_log.txt", $"TryGetNextFrame returned false at {expectedPosition}\n");
            return lastFrameBuffer;
        }
        catch (Exception ex)
        {
            File.AppendAllText("decoder_log.txt", $"ReadNextFrame Error at {expectedPosition}: {ex.Message}\n{ex.StackTrace}\n");
            return lastFrameBuffer;
        }
    }

    /// <summary>
    /// Gets the last decoded frame without advancing. Use when paused.
    /// </summary>
    public byte[]? GetCurrentFrame()
    {
        return lastFrameBuffer;
    }

    private TimeSpan ClampPosition(TimeSpan position)
    {
        if (position <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (Duration <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var fallbackFps = FrameRate > 0 ? FrameRate : 30.0;
        var frameStep = TimeSpan.FromSeconds(1.0 / fallbackFps);
        var maxSafePosition = Duration - frameStep;
        if (maxSafePosition < TimeSpan.Zero)
        {
            maxSafePosition = TimeSpan.Zero;
        }

        if (position >= Duration)
        {
            return maxSafePosition;
        }

        if (position > maxSafePosition)
        {
            return maxSafePosition;
        }

        return position;
    }

    private void CopyFrameToBuffer(ImageData frame)
    {
        var dataSpan = frame.Data;
        var bufferSize = dataSpan.Length;

        if (lastFrameBuffer is null || lastFrameBuffer.Length != bufferSize)
        {
            lastFrameBuffer = new byte[bufferSize];
        }

        dataSpan.CopyTo(lastFrameBuffer);
    }

    public void Close()
    {
        mediaFile?.Dispose();
        mediaFile = null;
        lastFrameBuffer = null;
        lastDecodedPosition = TimeSpan.FromSeconds(-1);
        isSequentialMode = false;
        FrameWidth = 0;
        FrameHeight = 0;
        Duration = TimeSpan.Zero;
        FrameRate = 0;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Close();
    }
}
