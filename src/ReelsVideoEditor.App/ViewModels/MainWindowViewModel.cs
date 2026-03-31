using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using ReelsVideoEditor.App.ViewModels.Effects;
using ReelsVideoEditor.App.ViewModels.Export;
using ReelsVideoEditor.App.ViewModels.Preview;
using ReelsVideoEditor.App.ViewModels.Text;
using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.VideoFiles;
using ReelsVideoEditor.App.ViewModels.Watermarks;

namespace ReelsVideoEditor.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private bool isSyncingPreviewTransformFromTimeline;

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
            ResolveVideoLayers = playbackMilliseconds => Timeline.ResolvePreviewVideoLayers(playbackMilliseconds),
            ResolveAudioState = playbackMilliseconds => Timeline.ResolvePreviewAudioState(playbackMilliseconds),
            HasSyntheticVideoContent = () => Timeline.HasVisibleTextOnlyPlaybackContent(),
            HasSelectedVideoClip = () => Timeline.HasSelectedVideoClip(),
            HasActiveTransformTarget = playbackMilliseconds => Timeline.HasActiveTransformTargetAt(playbackMilliseconds),
            IsTextTransformTarget = playbackMilliseconds => Timeline.IsTextTransformTargetAt(playbackMilliseconds),
            ResolvePlaybackMaxMilliseconds = () => Timeline.ResolvePlaybackDurationMilliseconds(),
            PlaybackTimeChanged = playbackMilliseconds => Timeline.UpdatePlayheadFromPlayback(playbackMilliseconds),
            PlaybackStateChanged = isPlaying => Timeline.SetPlaybackActive(isPlaying)
        };

        Preview.PropertyChanged += (_, args) =>
        {
            if (isSyncingPreviewTransformFromTimeline)
            {
                return;
            }

            if (args.PropertyName == nameof(PreviewViewModel.TransformX)
                || args.PropertyName == nameof(PreviewViewModel.TransformY)
                || args.PropertyName == nameof(PreviewViewModel.TransformScale)
                || args.PropertyName == nameof(PreviewViewModel.CropLeft)
                || args.PropertyName == nameof(PreviewViewModel.CropTop)
                || args.PropertyName == nameof(PreviewViewModel.CropRight)
                || args.PropertyName == nameof(PreviewViewModel.CropBottom))
            {
                Timeline.ApplyTransformToTarget(
                    Preview.TransformX,
                    Preview.TransformY,
                    Preview.TransformScale,
                    Preview.CropLeft,
                    Preview.CropTop,
                    Preview.CropRight,
                    Preview.CropBottom);
            }
        };

        Preview.UseBlurredBackground = Timeline.ShouldPreviewClipUseBlurredBackground();

        Export.PreviewContext = Preview;

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

        Timeline.PlaybackSeekRequested = seekMilliseconds =>
        {
            Preview.SeekToPlaybackPosition(seekMilliseconds);
        };

        Timeline.PreviewLevelsChanged = audioVolume =>
        {
            Preview.CurrentAudioVolume = audioVolume;
        };

        Text.ApplySelectedTextSettingsRequested = (text, colorHex, fontSize, fontFamily) =>
        {
            Timeline.UpdateSelectedTextClipSettings(text, colorHex, fontSize, fontFamily);
            Text.SyncSelectedTextClip(Timeline.ResolveSelectedTextClipState());
        };

        Timeline.TextOverlayStateChanged = state =>
        {
            Preview.UpdateTextOverlayState(state);
            Preview.RefreshRenderAvailability();
        };

        Timeline.PreviewClipChanged = () =>
        {
            var resolvedPath = Timeline.ResolvePreviewClipPath();
            Preview.UseBlurredBackground = Timeline.ShouldPreviewClipUseBlurredBackground();
            SyncPreviewTransformFromTimelineTarget();
            Preview.RefreshRenderAvailability();
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                Preview.Stop();
                Preview.SourceVideoPath = null;
            }
            else if (string.IsNullOrWhiteSpace(Preview.SourceVideoPath) || !File.Exists(Preview.SourceVideoPath))
            {
                Preview.SourceVideoPath = resolvedPath;
            }
        };

        Timeline.PreviewSelectionChanged = () =>
        {
            SyncPreviewTransformFromTimelineTarget();
            var selectedTextState = Timeline.ResolveSelectedTextClipState();
            Text.SyncSelectedTextClip(selectedTextState);
            if (selectedTextState.HasSelection)
            {
                SelectedSection = SidebarSection.Text;
            }
            if (!Preview.IsPlaying)
            {
                Preview.SeekToPlaybackPosition(Preview.CurrentPlaybackMilliseconds);
            }
        };

        SyncPreviewTransformFromTimelineTarget();
        Timeline.RefreshPreviewLevels();
        Timeline.RefreshTextOverlayState();
        Text.SyncSelectedTextClip(Timeline.ResolveSelectedTextClipState());
    }

    private void SyncPreviewTransformFromTimelineTarget()
    {
        var transformState = Timeline.ResolveTransformTargetState();
        isSyncingPreviewTransformFromTimeline = true;
        try
        {
            Preview.TransformX = transformState.TransformX;
            Preview.TransformY = transformState.TransformY;
            Preview.TransformScale = transformState.TransformScale;
            Preview.CropLeft = transformState.CropLeft;
            Preview.CropTop = transformState.CropTop;
            Preview.CropRight = transformState.CropRight;
            Preview.CropBottom = transformState.CropBottom;
        }
        finally
        {
            isSyncingPreviewTransformFromTimeline = false;
        }
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
