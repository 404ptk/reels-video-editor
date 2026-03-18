using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReelsVideoEditor.App.ViewModels.Effects;
using ReelsVideoEditor.App.ViewModels.Preview;
using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.VideoFiles;
using ReelsVideoEditor.App.ViewModels.Watermarks;
using System;

namespace ReelsVideoEditor.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private SidebarSection selectedSection = SidebarSection.Explorer;

    public PreviewViewModel Preview { get; }

    public VideoFilesViewModel VideoFiles { get; } = new();

    public TimelineViewModel Timeline { get; } = new();

    public EffectsViewModel Effects { get; } = new();

    public WatermarksViewModel Watermarks { get; } = new();

    public bool IsExplorerSection => SelectedSection == SidebarSection.Explorer;

    public bool IsEffectsSection => SelectedSection == SidebarSection.Effects;

    public bool IsWatermarksSection => SelectedSection == SidebarSection.Watermarks;

    public MainWindowViewModel()
    {
        Preview = new PreviewViewModel
        {
            ResolveVideoPath = () => Timeline.ResolvePreviewClipPath(),
            PlaybackTimeChanged = playbackMilliseconds => Timeline.UpdatePlayheadFromPlayback(playbackMilliseconds),
            PlaybackStateChanged = isPlaying => Timeline.SetPlaybackActive(isPlaying)
        };

        Timeline.PlayheadSeekRequested = seconds =>
        {
            var seekMilliseconds = (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
            Preview.SeekToPlaybackPosition(seekMilliseconds);
        };
    }

    [RelayCommand]
    private void ShowExplorer()
    {
        SelectedSection = SidebarSection.Explorer;
    }

    [RelayCommand]
    private void ShowEffects()
    {
        SelectedSection = SidebarSection.Effects;
    }

    [RelayCommand]
    private void ShowWatermarks()
    {
        SelectedSection = SidebarSection.Watermarks;
    }

    partial void OnSelectedSectionChanged(SidebarSection value)
    {
        OnPropertyChanged(nameof(IsExplorerSection));
        OnPropertyChanged(nameof(IsEffectsSection));
        OnPropertyChanged(nameof(IsWatermarksSection));
    }
}

public enum SidebarSection
{
    Explorer,
    Effects,
    Watermarks
}
