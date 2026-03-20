using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReelsVideoEditor.App.ViewModels.Effects;
using ReelsVideoEditor.App.ViewModels.Export;
using ReelsVideoEditor.App.ViewModels.Preview;
using ReelsVideoEditor.App.ViewModels.Text;
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

    public TextViewModel Text { get; } = new();

    public ExportViewModel Export { get; } = new();


    public bool IsExplorerSection => SelectedSection == SidebarSection.Explorer;

    public bool IsEffectsSection => SelectedSection == SidebarSection.Effects;

    public bool IsWatermarksSection => SelectedSection == SidebarSection.Watermarks;

    public bool IsTextSection => SelectedSection == SidebarSection.Text;

    public bool IsExportSection => SelectedSection == SidebarSection.Export;

    public bool IsEditorLayoutVisible => !IsExportSection;


    public MainWindowViewModel()
    {
        Export.TimelineContext = Timeline;
        
        Preview = new PreviewViewModel
        {
            ResolveVideoPath = () => Timeline.ResolvePreviewClipPath(),
            PlaybackTimeChanged = playbackMilliseconds => Timeline.UpdatePlayheadFromPlayback(playbackMilliseconds),
            PlaybackStateChanged = isPlaying => Timeline.SetPlaybackActive(isPlaying)
        };

        Timeline.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(TimelineViewModel.IsAudioMuted))
            {
                Preview.IsAudioMuted = Timeline.IsAudioMuted;
            }
            if (args.PropertyName == nameof(TimelineViewModel.IsVideoHidden))
            {
                Preview.IsVideoHidden = Timeline.IsVideoHidden;
            }
        };

        Timeline.PlayheadSeekRequested = seconds =>
        {
            var seekMilliseconds = (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
            Preview.SeekToPlaybackPosition(seekMilliseconds);
        };

        Timeline.PreviewLevelsChanged = audioVolume =>
        {
            Preview.CurrentAudioVolume = audioVolume;
        };

        Timeline.PreviewClipChanged = () =>
        {
            var resolvedPath = Timeline.ResolvePreviewClipPath();
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                Preview.Stop();
                Preview.CurrentVideoPath = null;
            }
            else if (Preview.CurrentVideoPath != resolvedPath)
            {
                Preview.Stop();
                Preview.CurrentVideoPath = resolvedPath;
            }
        };

        Timeline.RefreshPreviewLevels();
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

    [RelayCommand]
    private void ShowText()
    {
        SelectedSection = SidebarSection.Text;
    }

    [RelayCommand]
    private void ShowExport()
    {
        SelectedSection = SidebarSection.Export;
    }

    partial void OnSelectedSectionChanged(SidebarSection value)
    {
        OnPropertyChanged(nameof(IsExplorerSection));
        OnPropertyChanged(nameof(IsEffectsSection));
        OnPropertyChanged(nameof(IsWatermarksSection));
        OnPropertyChanged(nameof(IsTextSection));
        OnPropertyChanged(nameof(IsExportSection));
        OnPropertyChanged(nameof(IsEditorLayoutVisible));
    }
}

public enum SidebarSection
{
    Explorer,
    Effects,
    Watermarks,
    Text,
    Export,
}
