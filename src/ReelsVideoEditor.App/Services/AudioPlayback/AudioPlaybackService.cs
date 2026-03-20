using System;
using System.IO;
using NAudio.Wave;

namespace ReelsVideoEditor.App.Services.AudioPlayback;

public sealed class AudioPlaybackService : IDisposable
{
    private WaveOutEvent? waveOut;
    private MediaFoundationReader? audioReader;
    private bool disposed;

    public bool IsPlaying => waveOut?.PlaybackState == PlaybackState.Playing;

    public TimeSpan CurrentPosition => audioReader?.CurrentTime ?? TimeSpan.Zero;

    public TimeSpan TotalDuration => audioReader?.TotalTime ?? TimeSpan.Zero;

    private float volume = 1.0f;
    public float Volume
    {
        get => volume;
        set
        {
            volume = Math.Clamp(value, 0f, 1f);
            if (waveOut is not null)
            {
                waveOut.Volume = volume;
            }
        }
    }

    private bool isMuted;
    public bool IsMuted
    {
        get => isMuted;
        set
        {
            isMuted = value;
            if (waveOut is not null)
            {
                waveOut.Volume = isMuted ? 0f : volume;
            }
        }
    }

    public void Open(string path)
    {
        Close();

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            audioReader = new MediaFoundationReader(path);
            waveOut = new WaveOutEvent
            {
                DesiredLatency = 150
            };
            waveOut.Init(audioReader);
            waveOut.Volume = isMuted ? 0f : volume;
        }
        catch
        {
            Close();
        }
    }

    public void Play()
    {
        if (waveOut is null || audioReader is null)
        {
            return;
        }

        if (waveOut.PlaybackState == PlaybackState.Stopped && audioReader.Position >= audioReader.Length)
        {
            audioReader.Position = 0;
        }

        waveOut.Play();
    }

    public void Pause()
    {
        waveOut?.Pause();
    }

    public void Stop()
    {
        waveOut?.Stop();
        if (audioReader is not null)
        {
            audioReader.Position = 0;
        }
    }

    public void Seek(TimeSpan position)
    {
        if (audioReader is null)
        {
            return;
        }

        var clampedPosition = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        if (clampedPosition > audioReader.TotalTime)
        {
            clampedPosition = audioReader.TotalTime;
        }

        audioReader.CurrentTime = clampedPosition;
    }

    public void Close()
    {
        waveOut?.Stop();
        waveOut?.Dispose();
        waveOut = null;
        audioReader?.Dispose();
        audioReader = null;
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
