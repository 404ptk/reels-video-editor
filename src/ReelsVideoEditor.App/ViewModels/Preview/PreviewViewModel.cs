using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;

namespace ReelsVideoEditor.App.ViewModels.Preview;

public sealed partial class PreviewViewModel : ViewModelBase
{
    private const string ZeroTime = "00:00:00";

    public Func<string?>? ResolveVideoPath { get; set; }

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private string? currentVideoPath;

    [ObservableProperty]
    private int stopRequestVersion;

    [ObservableProperty]
    private string currentPlaybackTimeText = ZeroTime;

    [ObservableProperty]
    private string totalPlaybackTimeText = ZeroTime;

    public string Title { get; } = "Preview";

    public string PlaceholderTitle { get; } = "No video loaded";

    public string PlaceholderSubtitle { get; } = "Drop a video here";

    public string PlayPauseIconPath => IsPlaying
        ? "M4,3 H7 V13 H4 Z M9,3 H12 V13 H9 Z"
        : "M4,3 L13,8 L4,13 Z";

    public bool HasVideoLoaded => !string.IsNullOrWhiteSpace(CurrentVideoPath) && File.Exists(CurrentVideoPath);

    public bool ShowPlaceholder => !HasVideoLoaded;

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
    private void Stop()
    {
        IsPlaying = false;
        StopRequestVersion++;
        CurrentPlaybackTimeText = ZeroTime;
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayPauseIconPath));
    }

    partial void OnCurrentVideoPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasVideoLoaded));
        OnPropertyChanged(nameof(ShowPlaceholder));
        CurrentPlaybackTimeText = ZeroTime;
        TotalPlaybackTimeText = ZeroTime;
    }

    public void UpdatePlaybackTime(long playbackMilliseconds)
    {
        CurrentPlaybackTimeText = FormatPlaybackTime(playbackMilliseconds);
    }

    public void UpdateTotalPlaybackTime(long totalPlaybackMilliseconds)
    {
        if (totalPlaybackMilliseconds <= 0)
        {
            return;
        }

        TotalPlaybackTimeText = FormatPlaybackTime(totalPlaybackMilliseconds);
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
}
