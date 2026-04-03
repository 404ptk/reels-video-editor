using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.ViewModels.Preview;

public enum PreviewQuality
{
    High,
    Mid,
    Low
}

public sealed partial class PreviewViewModel : ViewModelBase
{
    private const string ZeroTime = "00:00:00";

    public Func<string?>? ResolveVideoPath { get; set; }

    public Func<long, IReadOnlyList<PreviewVideoLayer>>? ResolveVideoLayers { get; set; }

    public Func<long, PreviewAudioState>? ResolveAudioState { get; set; }

    public Func<bool>? HasSyntheticVideoContent { get; set; }

    public Func<bool>? HasSelectedVideoClip { get; set; }

    public Func<long, bool>? HasActiveTransformTarget { get; set; }

    public Func<long, bool>? IsTextTransformTarget { get; set; }

    public Func<long>? ResolvePlaybackMaxMilliseconds { get; set; }

    public Action<double, double>? PreviewFrameScaleChanged { get; set; }

    public Action<long>? PlaybackTimeChanged { get; set; }

    public Action<bool>? PlaybackStateChanged { get; set; }

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private string? sourceVideoPath;

    [ObservableProperty]
    private WriteableBitmap? currentFrame;

    [ObservableProperty]
    private string fpsText = "0 FPS";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FpsDotColor))]
    private int fps;

    public string FpsDotColor => Fps switch
    {
        >= 50 => "#00FF00",
        >= 25 => "#FFFF00",
        _ => "#FF0000"
    };

    [ObservableProperty]
    private string zoomText = "Zoom: 100%";

    [ObservableProperty]
    private PreviewQuality selectedQuality = PreviewQuality.Mid;

    [ObservableProperty]
    private bool isTransformModeEnabled;

    [ObservableProperty]
    private bool isClipperModeEnabled;

    [ObservableProperty]
    private bool isTransformTargetActive = true;

    public bool ShowTransformHandles => IsTransformModeEnabled && IsVideoVisible && IsTransformTargetActive;
    public bool ShowClipperHandles => IsClipperModeEnabled && IsVideoVisible && IsTransformTargetActive;

    partial void OnIsTransformModeEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowTransformHandles));
        if (value)
            IsClipperModeEnabled = false;
    }

    partial void OnIsClipperModeEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowClipperHandles));
        if (value)
            IsTransformModeEnabled = false;
    }

    public PreviewQuality[] AvailableQualities { get; } = new[] 
    { 
        PreviewQuality.High, 
        PreviewQuality.Mid, 
        PreviewQuality.Low 
    };

    [ObservableProperty]
    private bool isAudioMuted;

    [ObservableProperty]
    private bool isVideoHidden;

    [ObservableProperty]
    private bool useBlurredBackground = true;

    [ObservableProperty]
    private double currentAudioVolume = 1.0;

    [ObservableProperty]
    private int stopRequestVersion;

    [ObservableProperty]
    private long currentPlaybackMilliseconds;

    [ObservableProperty]
    private string currentPlaybackTimeText = ZeroTime;

    [ObservableProperty]
    private string totalPlaybackTimeText = ZeroTime;

    [ObservableProperty]
    private long requestedSeekMilliseconds;

    [ObservableProperty]
    private int seekRequestVersion;

    public string Title { get; } = "Preview";

    public string PlaceholderTitle { get; } = "No video loaded";

    public string PlaceholderSubtitle { get; } = "Drop a video here";

    public string PlayPauseIconPath => IsPlaying
        ? "M4,3 H7 V13 H4 Z M9,3 H12 V13 H9 Z"
        : "M4,3 L13,8 L4,13 Z";

    public bool HasVideoLoaded => !string.IsNullOrWhiteSpace(SourceVideoPath) && File.Exists(SourceVideoPath);

    public bool HasRenderableTimelineContent => HasVideoLoaded || (HasSyntheticVideoContent?.Invoke() ?? false);

    public bool ShowPlaceholder => !HasRenderableTimelineContent;

    public bool IsVideoVisible => HasRenderableTimelineContent && !IsVideoHidden;

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (!IsPlaying)
        {
            if (!HasVideoLoaded)
            {
                var resolvedPath = ResolveVideoPath?.Invoke();
                if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
                {
                    if (!HasRenderableTimelineContent)
                    {
                        return;
                    }
                }
                else
                {
                    SourceVideoPath = resolvedPath;
                }
            }

            if (!HasRenderableTimelineContent)
            {
                return;
            }

            IsPlaying = true;
            return;
        }

        IsPlaying = false;
    }

    [RelayCommand]
    public void Stop()
    {
        IsPlaying = false;
        StopRequestVersion++;
        CurrentPlaybackMilliseconds = 0;
        CurrentPlaybackTimeText = ZeroTime;
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayPauseIconPath));
        PlaybackStateChanged?.Invoke(value);
    }

    partial void OnSourceVideoPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasVideoLoaded));
        OnPropertyChanged(nameof(HasRenderableTimelineContent));
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(IsVideoVisible));
        OnPropertyChanged(nameof(ShowTransformHandles));
        OnPropertyChanged(nameof(ShowClipperHandles));
        CurrentPlaybackMilliseconds = 0;
        CurrentPlaybackTimeText = ZeroTime;
        TotalPlaybackTimeText = ZeroTime;
    }

    partial void OnIsVideoHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVideoVisible));
        OnPropertyChanged(nameof(ShowTransformHandles));
        OnPropertyChanged(nameof(ShowClipperHandles));
    }

    partial void OnIsTransformTargetActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowTransformHandles));
        OnPropertyChanged(nameof(ShowClipperHandles));
    }

    public void UpdatePlaybackTime(long playbackMilliseconds)
    {
        var safeMilliseconds = Math.Max(0, playbackMilliseconds);
        CurrentPlaybackMilliseconds = safeMilliseconds;
        CurrentPlaybackTimeText = FormatPlaybackTime(safeMilliseconds);
    }

    public void UpdateTotalPlaybackTime(long totalPlaybackMilliseconds)
    {
        if (totalPlaybackMilliseconds <= 0)
        {
            return;
        }

        TotalPlaybackTimeText = FormatPlaybackTime(totalPlaybackMilliseconds);
    }

    public void SeekToPlaybackPosition(long targetPlaybackMilliseconds)
    {
        if (!HasVideoLoaded)
        {
            var resolvedPath = ResolveVideoPath?.Invoke();
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                if (!HasRenderableTimelineContent)
                {
                    return;
                }
            }
            else
            {
                SourceVideoPath = resolvedPath;
            }
        }

        if (!HasRenderableTimelineContent)
        {
            return;
        }

        var safeMilliseconds = Math.Max(0, targetPlaybackMilliseconds);
        RequestedSeekMilliseconds = safeMilliseconds;
        SeekRequestVersion++;
        UpdatePlaybackTime(safeMilliseconds);
    }

    public void RefreshRenderAvailability()
    {
        OnPropertyChanged(nameof(HasRenderableTimelineContent));
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(IsVideoVisible));
        OnPropertyChanged(nameof(ShowTransformHandles));
        OnPropertyChanged(nameof(ShowClipperHandles));
    }

    public void NotifyPreviewFrameScaleChanged(double ratioX, double ratioY)
    {
        if (double.IsNaN(ratioX) || double.IsNaN(ratioY) || ratioX <= 0 || ratioY <= 0)
        {
            return;
        }

        PreviewFrameScaleChanged?.Invoke(ratioX, ratioY);
    }

    private static string FormatPlaybackTime(long playbackMilliseconds)
    {
        var safeMilliseconds = Math.Max(0, playbackMilliseconds);
        var totalCentiseconds = safeMilliseconds / 10;
        var minutes = totalCentiseconds / 6000;
        var seconds = (totalCentiseconds / 100) % 60;
        var centiseconds = totalCentiseconds % 100;

        return $"{minutes:D2}:{seconds:D2}:{centiseconds:D2}";
    }

    partial void OnCurrentPlaybackMillisecondsChanged(long value)
    {
        PlaybackTimeChanged?.Invoke(value);
    }

    public void TriggerFrameUpdate()
    {
        OnPropertyChanged(nameof(CurrentFrame));
    }
}
