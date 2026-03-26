using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using ReelsVideoEditor.App.Services.Composition;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.ViewModels.Timeline;

public partial class TimelineViewModel : ViewModelBase
{
    private const int TimelineDurationSeconds = 300;
    private const double BaseTickWidth = 14;
    private static readonly int[] LabelIntervalsInSeconds = [1, 2, 5, 10, 15, 30, 60, 120, 300];
    private const int MinZoom = 25;
    private const int MaxZoom = 300;
    private const double TickSectionHeight = 52;
    private const double TrackTopSpacing = 10;
    private const double TrackGap = 8;
    private const double LaneHeaderHeight = 18;
    private const double LaneClipVerticalPadding = 12;
    private const int MinLaneContentHeight = 30;
    private const int MaxLaneContentHeight = 140;
    private const int MaxVideoLines = 9;

    private readonly Stack<Action> undoStack = new();
    private readonly TimelineCompositionPlanner compositionPlanner = new();
    private bool isBatchUpdatingClips;

    [ObservableProperty]
    private int zoomPercent = 100;

    [ObservableProperty]
    private bool isAudioMuted;

    [ObservableProperty]
    private double playheadSeconds;

    [ObservableProperty]
    private bool isPlaybackActive;

    [ObservableProperty]
    private int laneContentHeight = 46;

    private long lastPlaybackMilliseconds = -1;
    private double playbackMaxSeconds = TimelineDurationSeconds;
    private TimelineClipItem? lastPreviewClip;

    public Action<long>? PlaybackSeekRequested { get; set; }

    public Action? PreviewClipChanged { get; set; }

    public Action? PreviewSelectionChanged { get; set; }

    public Action<double>? PreviewLevelsChanged { get; set; }

    public ObservableCollection<TimelineMinorTick> MinorTicks { get; } = [];

    public ObservableCollection<TimelineMajorTick> MajorTicks { get; } = [];

    public ObservableCollection<TimelineClipItem> VideoClips { get; } = [];

    public ObservableCollection<TimelineClipItem> AudioClips { get; } = [];

    public ObservableCollection<VideoLaneItem> VideoLanes { get; } = [new("VIDEO", true, false, false)];

    public ObservableCollection<AudioLaneItem> AudioLanes { get; } = [];

    public ObservableCollection<TimelineClipItem> Clips => VideoClips;

    public double TickWidth => BaseTickWidth * ZoomPercent / 100.0;

    public double TimelineCanvasWidth => TickWidth * TimelineDurationSeconds;

    public double LaneContainerHeight => LaneHeaderHeight + LaneContentHeight;

    public double ClipVisualHeight => Math.Max(18, LaneContainerHeight - LaneClipVerticalPadding);

    public int VideoLaneCount => VideoLanes.Count;

    public int AudioLaneCount => AudioLanes.Count;

    public bool CanAddNewLine => VideoLaneCount < MaxVideoLines;

    public bool IsVideoSolo => VideoLanes.FirstOrDefault()?.IsSolo ?? false;

    public bool IsVideoHidden => VideoLanes.FirstOrDefault()?.IsHidden ?? false;

    public double TimelineCanvasHeight => TickSectionHeight
        + TrackTopSpacing
        + (VideoLaneCount * LaneContainerHeight)
        + (VideoLaneCount * TrackGap)
        + (AudioLaneCount * LaneContainerHeight)
        + (Math.Max(0, AudioLaneCount - 1) * TrackGap);

    public double PlayheadHeight => TimelineCanvasHeight;

    public double PlayheadLeft => Math.Clamp(PlayheadSeconds, 0, TimelineDurationSeconds) * TickWidth;

    public double PlayheadVisualLeft => 10 + PlayheadLeft;

    public bool HasClips => VideoClips.Count > 0;

    public static bool IsPlayheadVisible => true;

    private static double TimelineBaseWidth => BaseTickWidth * TimelineDurationSeconds;

    public TimelineViewModel()
    {
        VideoClips.CollectionChanged += OnVideoClipsChanged;
        VideoLanes.CollectionChanged += OnVideoLanesChanged;
        AudioLanes.CollectionChanged += OnAudioLanesChanged;
        foreach (var lane in VideoLanes)
        {
            lane.PropertyChanged += OnVideoLanePropertyChanged;
        }

        RebuildAudioLaneCollections();
        BuildMinorTicks();
        RebuildMajorTicks();
    }

    private void OnVideoLanesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is VideoLaneItem lane)
                {
                    lane.PropertyChanged -= OnVideoLanePropertyChanged;
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is VideoLaneItem lane)
                {
                    lane.PropertyChanged += OnVideoLanePropertyChanged;
                }
            }
        }

        OnPropertyChanged(nameof(VideoLaneCount));
        OnPropertyChanged(nameof(AudioLaneCount));
        OnPropertyChanged(nameof(CanAddNewLine));
        OnPropertyChanged(nameof(TimelineCanvasHeight));
        OnPropertyChanged(nameof(PlayheadHeight));
        OnPropertyChanged(nameof(IsVideoSolo));
        OnPropertyChanged(nameof(IsVideoHidden));
        RemoveLineCommand.NotifyCanExecuteChanged();
        RebuildAudioLaneCollections();
        RebuildLaneClipCollections();
        RebuildAudioLaneClipCollections();
        NotifyPreviewClipIfChanged();
        UpdatePreviewLevels();
    }

    private void OnVideoLanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not VideoLaneItem lane)
        {
            return;
        }

        if (e.PropertyName == nameof(VideoLaneItem.IsSolo) || e.PropertyName == nameof(VideoLaneItem.IsHidden))
        {
            NotifyPreviewClipIfChanged();
            UpdatePreviewLevels();
        }

        if (!lane.IsPrimary)
        {
            return;
        }

        if (e.PropertyName == nameof(VideoLaneItem.IsSolo))
        {
            OnPropertyChanged(nameof(IsVideoSolo));
            return;
        }

        if (e.PropertyName == nameof(VideoLaneItem.IsHidden))
        {
            OnPropertyChanged(nameof(IsVideoHidden));
        }
    }

    private void OnAudioLanesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is AudioLaneItem lane)
                {
                    lane.PropertyChanged -= OnAudioLanePropertyChanged;
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is AudioLaneItem lane)
                {
                    lane.PropertyChanged += OnAudioLanePropertyChanged;
                }
            }
        }
    }

    private void OnAudioLanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AudioLaneItem.IsMuted) && e.PropertyName != nameof(AudioLaneItem.IsSolo))
        {
            return;
        }

        NotifyPreviewClipIfChanged();
        UpdatePreviewLevels();
    }
}
