using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;

namespace ReelsVideoEditor.App.ViewModels.Preview;

public sealed partial class PreviewViewModel : ViewModelBase
{
    public Func<string?>? ResolveVideoPath { get; set; }

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private string? currentVideoPath;

    [ObservableProperty]
    private int stopRequestVersion;

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
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayPauseIconPath));
    }

    partial void OnCurrentVideoPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasVideoLoaded));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }
}
