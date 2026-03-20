using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;

namespace ReelsVideoEditor.App.ViewModels.Preview;

public sealed partial class PreviewViewModel : ViewModelBase
{
    private const string ZeroTime = "00:00:00";

    public Func<string?>? ResolveVideoPath { get; set; }

    public Action<long>? PlaybackTimeChanged { get; set; }

    public Action<bool>? PlaybackStateChanged { get; set; }

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private string? currentVideoPath;

    [ObservableProperty]
    private bool isAudioMuted;

    [ObservableProperty]
    private bool isVideoHidden;

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

    public bool HasVideoLoaded => !string.IsNullOrWhiteSpace(CurrentVideoPath) && File.Exists(CurrentVideoPath);

    public bool ShowPlaceholder => !HasVideoLoaded;

    public bool IsVideoVisible => HasVideoLoaded && !IsVideoHidden;

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
                    return;
                }

                CurrentVideoPath = resolvedPath;
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

    partial void OnCurrentVideoPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasVideoLoaded));
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(IsVideoVisible));
        CurrentPlaybackMilliseconds = 0;
        CurrentPlaybackTimeText = ZeroTime;
        TotalPlaybackTimeText = ZeroTime;
    }

    partial void OnIsVideoHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVideoVisible));
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
                return;
            }

            CurrentVideoPath = resolvedPath;
        }

        var safeMilliseconds = Math.Max(0, targetPlaybackMilliseconds);
        RequestedSeekMilliseconds = safeMilliseconds;
        SeekRequestVersion++;
        UpdatePlaybackTime(safeMilliseconds);
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
}
