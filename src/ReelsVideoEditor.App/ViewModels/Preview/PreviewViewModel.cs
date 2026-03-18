using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ReelsVideoEditor.App.ViewModels.Preview;

public sealed partial class PreviewViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool isPlaying;

    public string Title { get; } = "Preview";

    public string PlaceholderTitle { get; } = "No video loaded";

    public string PlaceholderSubtitle { get; } = "Drop a video here";

    public string PlayPauseIconPath => IsPlaying
        ? "M4,3 H7 V13 H4 Z M9,3 H12 V13 H9 Z"
        : "M4,3 L13,8 L4,13 Z";

    [RelayCommand]
    private void TogglePlayPause()
    {
        IsPlaying = !IsPlaying;
    }

    [RelayCommand]
    private void Stop()
    {
        IsPlaying = false;
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayPauseIconPath));
    }
}
