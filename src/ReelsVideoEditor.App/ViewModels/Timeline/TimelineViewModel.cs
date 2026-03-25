using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReelsVideoEditor.App.Services.Composition;
using ReelsVideoEditor.App.Services.Export;
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

    // Compatibility surface used by preview/export; maps to primary lane state.
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

    partial void OnZoomPercentChanged(int value)
    {
        OnPropertyChanged(nameof(TickWidth));
        OnPropertyChanged(nameof(TimelineCanvasWidth));
        OnPropertyChanged(nameof(PlayheadLeft));
        OnPropertyChanged(nameof(PlayheadVisualLeft));
        OnPropertyChanged(nameof(CutterMarkerLeft));
        OnPropertyChanged(nameof(CutterMarkerVisualLeft));
        OnPropertyChanged(nameof(CutterMarkerContainerLeft));
        RebuildMajorTicks();
        TimelineClipArrangementService.RebuildLayouts(VideoClips, TickWidth);
        TimelineClipArrangementService.RebuildLayouts(AudioClips, TickWidth);
    }

    partial void OnPlayheadSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(PlayheadLeft));
        OnPropertyChanged(nameof(PlayheadVisualLeft));
        NotifyPreviewClipIfChanged();
        UpdatePreviewLevels();
    }

    partial void OnLaneContentHeightChanged(int value)
    {
        OnPropertyChanged(nameof(LaneContainerHeight));
        OnPropertyChanged(nameof(ClipVisualHeight));
        OnPropertyChanged(nameof(TimelineCanvasHeight));
        OnPropertyChanged(nameof(PlayheadHeight));
        RefreshClipLevelLines();
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

    partial void OnIsPlaybackActiveChanged(bool value)
    {
    }

    public void AddClipFromExplorer(string name, string path, double durationSeconds, double dropX, string? targetLaneLabel = null)
    {
        var clip = TimelineClipArrangementService.BuildClip(name, path, durationSeconds, dropX, TickWidth, TimelineDurationSeconds);
        var targetLane = ResolveLaneByLabel(targetLaneLabel) ?? ResolvePrimaryVideoLane();
        clip.VideoLaneLabel = targetLane?.Label ?? string.Empty;
        VideoClips.Add(clip);

        if (!IsStillImagePath(path))
        {
            var linkedAudio = TimelineClipArrangementService.BuildLinkedAudioClip(clip);
            AudioClips.Add(linkedAudio);
            _ = LoadAudioWaveformAsync(linkedAudio);
        }

        RebuildAudioLaneClipCollections();

        if (VideoClips.Count == 1)
        {
            PlayheadSeconds = clip.StartSeconds;
        }

        RefreshClipLevelLines();

        undoStack.Push(() =>
        {
            VideoClips.Remove(clip);
        });
    }

    public string? ResolvePreviewClipPath()
    {
        return ResolvePreviewClip()?.Path;
    }

    public IReadOnlyList<PreviewVideoLayer> ResolvePreviewVideoLayers(long playbackMilliseconds)
    {
        var timelineSeconds = ResolveTimelineSecondsForLayerPlayback(playbackMilliseconds);
        var hasAnySelectedVideoClip = VideoClips.Any(clip => clip.IsSelected);
        var plan = BuildCompositionPlan();
        var activeLayers = compositionPlanner.ResolveActiveVideoLayers(plan, timelineSeconds);
        if (activeLayers.Count == 0)
        {
            return [];
        }

        var result = new List<PreviewVideoLayer>(activeLayers.Count);
        for (var i = 0; i < activeLayers.Count; i++)
        {
            var layer = activeLayers[i];
            var clip = layer.Clip;

            result.Add(new PreviewVideoLayer(
                clip.Path,
                layer.PlaybackMilliseconds,
                layer.DrawBlurredBackground,
                IsSelected: clip.IsSelected,
                HasAnySelectedVideoClip: hasAnySelectedVideoClip,
                TransformX: clip.TransformX,
                TransformY: clip.TransformY,
                TransformScale: clip.TransformScale,
                CropLeft: clip.CropLeft,
                CropTop: clip.CropTop,
                CropRight: clip.CropRight,
                CropBottom: clip.CropBottom));
        }

        return result;
    }

    public PreviewAudioState ResolvePreviewAudioState(long playbackMilliseconds)
    {
        if (IsAudioMuted)
        {
            return PreviewAudioState.Silent;
        }

        var timelineSeconds = ResolveTimelineSecondsForLayerPlayback(playbackMilliseconds);
        var activeAudio = ResolveActiveAudioClipAt(timelineSeconds);
        if (activeAudio is null)
        {
            return PreviewAudioState.Silent;
        }

        var localSeconds = Math.Clamp(timelineSeconds - activeAudio.StartSeconds, 0, activeAudio.DurationSeconds);
        var localMilliseconds = (long)Math.Round(localSeconds * 1000, MidpointRounding.AwayFromZero);
        var volume = Math.Clamp(activeAudio.VolumeLevel, 0.0, 1.0);

        return new PreviewAudioState(activeAudio.Path, localMilliseconds, volume, ShouldPlay: true);
    }

    public IReadOnlyList<ExportAudioClipInput> ResolveExportAudioInputs()
    {
        return compositionPlanner.BuildExportAudioInputs(ResolveActiveAudioClips());
    }

    public long ResolvePlaybackDurationMilliseconds()
    {
        var durationSeconds = ResolvePlaybackDurationSeconds();
        return (long)Math.Round(durationSeconds * 1000, MidpointRounding.AwayFromZero);
    }

    public bool ShouldPreviewClipUseBlurredBackground()
    {
        var previewClip = ResolvePreviewClip();
        if (previewClip is null)
        {
            return true;
        }

        var lane = ResolveLaneByLabel(previewClip.VideoLaneLabel) ?? ResolvePrimaryVideoLane();
        if (lane is null)
        {
            return true;
        }

        // Foreground overlays on upper lanes should not generate their own blurred fill.
        return lane.IsPrimary;
    }

    public bool HasSelectedVideoClip()
    {
        return VideoClips.Any(clip => clip.IsSelected);
    }

    public PreviewClipTransform ResolveTransformTargetState()
    {
        var targetClip = ResolveTransformTargetClip();
        if (targetClip is null)
        {
            return PreviewClipTransform.Default;
        }

        return new PreviewClipTransform(
            targetClip.TransformX,
            targetClip.TransformY,
            targetClip.TransformScale,
            targetClip.CropLeft,
            targetClip.CropTop,
            targetClip.CropRight,
            targetClip.CropBottom);
    }

    public void ApplyTransformToTarget(
        double transformX,
        double transformY,
        double transformScale,
        double cropLeft,
        double cropTop,
        double cropRight,
        double cropBottom)
    {
        var targetClip = ResolveTransformTargetClip();
        if (targetClip is null)
        {
            return;
        }

        targetClip.TransformX = transformX;
        targetClip.TransformY = transformY;
        targetClip.TransformScale = Math.Max(0.1, transformScale);
        targetClip.CropLeft = Math.Clamp(cropLeft, 0.0, 0.95);
        targetClip.CropTop = Math.Clamp(cropTop, 0.0, 0.95);
        targetClip.CropRight = Math.Clamp(cropRight, 0.0, 0.95);
        targetClip.CropBottom = Math.Clamp(cropBottom, 0.0, 0.95);
    }

    public void UpdatePlayheadFromPlayback(long playbackMilliseconds)
    {
        if (!IsPlaybackActive)
        {
            return;
        }

        var safePlaybackMilliseconds = Math.Max(0, playbackMilliseconds);

        if (lastPlaybackMilliseconds >= 0)
        {
            safePlaybackMilliseconds = Math.Max(safePlaybackMilliseconds, lastPlaybackMilliseconds);
        }

        lastPlaybackMilliseconds = safePlaybackMilliseconds;

        var playbackSeconds = safePlaybackMilliseconds / 1000.0;
        var clampedSeconds = Math.Clamp(playbackSeconds, 0, playbackMaxSeconds);
        PlayheadSeconds = clampedSeconds;
    }

    public void SeekToPosition(double pointerX)
    {
        var playheadSeconds = Math.Max(0, (pointerX - 10) / TickWidth);
        var clampedSeconds = Math.Clamp(playheadSeconds, 0, TimelineDurationSeconds);

        PlayheadSeconds = clampedSeconds;
        lastPlaybackMilliseconds = -1;
        PlaybackSeekRequested?.Invoke(ResolvePlaybackSeekMilliseconds(clampedSeconds));
    }

    public void SetPlaybackActive(bool isPlaying)
    {
        IsPlaybackActive = isPlaying;
        lastPlaybackMilliseconds = -1;

        if (!isPlaying)
        {
            return;
        }

        playbackMaxSeconds = ResolvePlaybackDurationSeconds();
    }

    public void MoveClipToStart(TimelineClipItem clip, double requestedStartSeconds, string? targetLaneLabel = null)
    {
        var maxStartSeconds = Math.Max(0, TimelineDurationSeconds - clip.DurationSeconds);
        var clampedStartSeconds = Math.Clamp(requestedStartSeconds, 0, maxStartSeconds);
        var targetLane = ResolveLaneByLabel(targetLaneLabel);
        var laneChanged = targetLane is not null && !string.Equals(clip.VideoLaneLabel, targetLane.Label, StringComparison.Ordinal);

        if (Math.Abs(clampedStartSeconds - clip.StartSeconds) < 0.0001)
        {
            if (!laneChanged)
            {
                return;
            }

            clip.VideoLaneLabel = targetLane!.Label;
            var laneOnlyLinkedAudio = AudioClips.FirstOrDefault(audio => audio.LinkId == clip.LinkId);
            if (laneOnlyLinkedAudio is not null)
            {
                laneOnlyLinkedAudio.VideoLaneLabel = targetLane.Label;
            }
            RebuildLaneClipCollections();
            RebuildAudioLaneClipCollections();
            NotifyPreviewClipIfChanged();
            return;
        }

        clip.StartSeconds = clampedStartSeconds;
        if (laneChanged)
        {
            clip.VideoLaneLabel = targetLane!.Label;
        }

        TimelineClipArrangementService.RebuildLayouts([clip], TickWidth);

        var linkedAudio = AudioClips.FirstOrDefault(audio => audio.LinkId == clip.LinkId);
        if (linkedAudio is not null)
        {
            linkedAudio.StartSeconds = clampedStartSeconds;
            if (laneChanged)
            {
                linkedAudio.VideoLaneLabel = targetLane!.Label;
            }
            TimelineClipArrangementService.RebuildLayouts([linkedAudio], TickWidth);
        }

        if (laneChanged)
        {
            RebuildLaneClipCollections();
            RebuildAudioLaneClipCollections();
        }

        NotifyPreviewClipIfChanged();
        UpdatePreviewLevels();
    }

    public void CommitClipMove(TimelineClipItem clip, double previousStartSeconds, string previousLaneLabel)
    {
        var currentStartSeconds = clip.StartSeconds;
        var laneChanged = !string.Equals(clip.VideoLaneLabel, previousLaneLabel, StringComparison.Ordinal);
        if (Math.Abs(currentStartSeconds - previousStartSeconds) < 0.0001 && !laneChanged)
        {
            return;
        }

        undoStack.Push(() =>
        {
            MoveClipToStart(clip, previousStartSeconds, previousLaneLabel);
        });
    }

    public void RefreshPreviewLevels()
    {
        UpdatePreviewLevels();
    }

    public void SetAudioClipVolume(TimelineClipItem clip, double volumeLevel)
    {
        clip.VolumeLevel = Math.Clamp(volumeLevel, 0.0, 1.0);
        UpdateAudioClipLevelLine(clip);
        UpdatePreviewLevels();
    }

    public void ChangeZoomFromWheel(double wheelDelta, double viewportWidth)
    {
        if (wheelDelta == 0)
        {
            return;
        }

        var step = wheelDelta > 0 ? 10 : -10;
        var minZoomForViewport = ResolveMinZoomForViewport(viewportWidth);
        var nextZoom = Math.Clamp(ZoomPercent + step, minZoomForViewport, MaxZoom);

        if (nextZoom != ZoomPercent)
        {
            ZoomPercent = nextZoom;
        }
    }

    public void ChangeLaneHeightFromWheel(double wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return;
        }

        var step = wheelDelta > 0 ? 6 : -6;
        var nextHeight = Math.Clamp(LaneContentHeight + step, MinLaneContentHeight, MaxLaneContentHeight);

        if (nextHeight != LaneContentHeight)
        {
            LaneContentHeight = nextHeight;
        }
    }

    private static int ResolveMinZoomForViewport(double viewportWidth)
    {
        if (viewportWidth <= 0)
        {
            return MinZoom;
        }

        var fitZoom = (int)Math.Ceiling((viewportWidth / TimelineBaseWidth) * 100);
        return Math.Clamp(fitZoom, MinZoom, MaxZoom);
    }

    private void BuildMinorTicks()
    {
        for (var second = 0; second < TimelineDurationSeconds; second++)
        {
            MinorTicks.Add(new TimelineMinorTick(second > 0));
        }
    }

    private void RebuildMajorTicks()
    {
        MajorTicks.Clear();
        var labelIntervalSeconds = ResolveLabelIntervalSeconds();

        for (var second = 0; second < TimelineDurationSeconds; second += labelIntervalSeconds)
        {
            var label = $"{second / 60:D2}:{second % 60:D2}";
            var remaining = TimelineDurationSeconds - second;
            var segmentSeconds = Math.Min(labelIntervalSeconds, remaining);

            if (segmentSeconds <= 0)
            {
                continue;
            }

            var width = segmentSeconds * TickWidth;

            MajorTicks.Add(new TimelineMajorTick(label, width));
        }
    }

    private int ResolveLabelIntervalSeconds()
    {
        var rawInterval = (int)Math.Ceiling(90 / TickWidth);

        foreach (var candidate in LabelIntervalsInSeconds)
        {
            if (candidate >= rawInterval)
            {
                return candidate;
            }
        }

        return LabelIntervalsInSeconds[^1];
    }

    private void OnVideoClipsChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (isBatchUpdatingClips)
        {
            return;
        }

        RebuildLaneClipCollections();

        OnPropertyChanged(nameof(HasClips));
        PreviewClipChanged?.Invoke();

        if (VideoClips.Count == 0)
        {
            AudioClips.Clear();
            RebuildAudioLaneClipCollections();
            PlayheadSeconds = 0;
            RefreshClipLevelLines();
            UpdatePreviewLevels();
            return;
        }

        if (eventArgs.Action == NotifyCollectionChangedAction.Add && eventArgs.NewItems is { Count: > 0 })
        {
            RebuildAudioLaneClipCollections();
            RefreshClipLevelLines();
            UpdatePreviewLevels();
            return;
        }

        RebuildAudioFromVideo();

        PlayheadSeconds = ResolvePreviewClipStartSeconds();
        RefreshClipLevelLines();
        UpdatePreviewLevels();
    }

    private void RebuildAudioFromVideo()
    {
        var volumeByKey = AudioClips.ToDictionary(
            clip => BuildAudioClipKey(clip),
            clip => clip.VolumeLevel);

        AudioClips.Clear();

        foreach (var videoClip in VideoClips)
        {
            if (IsStillImagePath(videoClip.Path))
            {
                continue;
            }

            var audioClip = TimelineClipArrangementService.BuildLinkedAudioClip(videoClip);

            var key = BuildAudioClipKey(audioClip);
            if (volumeByKey.TryGetValue(key, out var volumeLevel))
            {
                audioClip.VolumeLevel = volumeLevel;
            }

            AudioClips.Add(audioClip);
            _ = LoadAudioWaveformAsync(audioClip);
        }

        RebuildAudioLaneClipCollections();
        RefreshClipLevelLines();
    }

    private async Task LoadAudioWaveformAsync(TimelineClipItem audioClip)
    {
        var waveform = await TimelineWaveformRenderService.TryRenderWaveformAsync(audioClip.Path);
        if (waveform is null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => audioClip.WaveformImage = waveform);
    }

    private double ResolvePreviewClipStartSeconds()
    {
        return ResolvePreviewClip()?.StartSeconds ?? 0;
    }

    private TimelineClipItem? ResolvePreviewClip()
    {
        if (VideoClips.Count == 0)
        {
            return null;
        }

        var plan = BuildCompositionPlan();
        return compositionPlanner.ResolvePreviewClip(plan, PlayheadSeconds);
    }

    private TimelineClipItem? ResolveTransformTargetClip()
    {
        var selectedClip = ResolveSelectedVideoClip();
        if (selectedClip is not null)
        {
            return selectedClip;
        }

        return ResolvePreviewClip();
    }

    private TimelineClipItem? ResolveSelectedVideoClip()
    {
        var selected = VideoClips.Where(clip => clip.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return null;
        }

        var activeSelected = selected
            .OrderBy(clip => ResolveLaneLayerIndex(clip.VideoLaneLabel))
            .ThenByDescending(clip => clip.StartSeconds)
            .FirstOrDefault(clip =>
                PlayheadSeconds >= clip.StartSeconds
                && PlayheadSeconds <= clip.StartSeconds + clip.DurationSeconds);
        if (activeSelected is not null)
        {
            return activeSelected;
        }

        return selected
            .OrderBy(clip => ResolveLaneLayerIndex(clip.VideoLaneLabel))
            .ThenBy(clip => clip.StartSeconds)
            .FirstOrDefault();
    }

    private IEnumerable<TimelineClipItem> ResolveVisibleVideoClips()
    {
        var plan = BuildCompositionPlan();
        foreach (var item in plan.VisibleVideoClips)
        {
            yield return item.Clip;
        }
    }

    private int ResolveLaneLayerIndex(string laneLabel)
    {
        return compositionPlanner.ResolveLaneLayerIndex(BuildCompositionPlan(), laneLabel);
    }

    private long ResolvePlaybackSeekMilliseconds(double timelineSeconds)
    {
        return (long)Math.Round(Math.Clamp(timelineSeconds, 0, TimelineDurationSeconds) * 1000, MidpointRounding.AwayFromZero);
    }

    private double ResolveTimelineSecondsForLayerPlayback(long playbackMilliseconds)
    {
        return Math.Clamp(playbackMilliseconds / 1000.0, 0, TimelineDurationSeconds);
    }

    private double ResolvePlaybackDurationSeconds()
    {
        var plan = BuildCompositionPlan();
        return compositionPlanner.ResolvePlaybackDurationSeconds(plan, ResolveActiveAudioClips(), IsAudioMuted, TimelineDurationSeconds);
    }

    private TimelineCompositionPlan BuildCompositionPlan()
    {
        return compositionPlanner.BuildPlan(VideoClips, VideoLanes);
    }

    private void NotifyPreviewClipIfChanged()
    {
        var previewClip = ResolvePreviewClip();
        if (ReferenceEquals(previewClip, lastPreviewClip))
        {
            return;
        }

        lastPreviewClip = previewClip;
        PreviewClipChanged?.Invoke();
    }

    public void SelectClipsInBox(double startX, double endX)
    {
        double minX = Math.Min(startX, endX);
        double maxX = Math.Max(startX, endX);

        var changed = false;

        foreach (var clip in VideoClips)
        {
            var nextValue = (clip.Left < maxX && (clip.Left + clip.Width) > minX);
            if (clip.IsSelected != nextValue)
            {
                clip.IsSelected = nextValue;
                changed = true;
            }
        }
        foreach (var clip in AudioClips)
        {
            var nextValue = (clip.Left < maxX && (clip.Left + clip.Width) > minX);
            if (clip.IsSelected != nextValue)
            {
                clip.IsSelected = nextValue;
                changed = true;
            }
        }

        if (changed)
        {
            PreviewSelectionChanged?.Invoke();
        }
    }

    public void DeleteSelectedClips()
    {
        var videoToRemove = VideoClips.Where(c => c.IsSelected).ToList();
        var audioToRemoveNames = AudioClips.Where(c => c.IsSelected).Select(c => c.Name).ToList();

        var combinedToRemove = VideoClips
            .Where(c => c.IsSelected || audioToRemoveNames.Contains(c.Name))
            .ToList();

        if (combinedToRemove.Count == 0) return;

        var removedClips = combinedToRemove.ToList();

        undoStack.Push(() =>
        {
            foreach (var clip in removedClips)
            {
                clip.IsSelected = false;
                VideoClips.Add(clip);
                
                var linkedAudio = TimelineClipArrangementService.BuildLinkedAudioClip(clip);
                AudioClips.Add(linkedAudio);
                _ = LoadAudioWaveformAsync(linkedAudio);
            }
        });

        foreach (var clip in combinedToRemove)
        {
            VideoClips.Remove(clip);
        }
    }

    public void Undo()
    {
        if (undoStack.Count > 0)
        {
            var action = undoStack.Pop();
            action();
        }
    }

    public void ClearSelection()
    {
        var changed = false;
        foreach (var clip in VideoClips)
        {
            if (clip.IsSelected)
            {
                clip.IsSelected = false;
                changed = true;
            }
        }

        foreach (var clip in AudioClips)
        {
            if (clip.IsSelected)
            {
                clip.IsSelected = false;
                changed = true;
            }
        }

        if (changed)
        {
            PreviewSelectionChanged?.Invoke();
        }
    }

    public void SelectSingleVideoClip(TimelineClipItem selectedClip)
    {
        var changed = false;
        foreach (var clip in VideoClips)
        {
            var nextValue = ReferenceEquals(clip, selectedClip);
            if (clip.IsSelected != nextValue)
            {
                clip.IsSelected = nextValue;
                changed = true;
            }
        }

        foreach (var clip in AudioClips)
        {
            var nextValue = clip.LinkId == selectedClip.LinkId;
            if (clip.IsSelected != nextValue)
            {
                clip.IsSelected = nextValue;
                changed = true;
            }
        }

        if (changed)
        {
            PreviewSelectionChanged?.Invoke();
        }
    }

    private void UpdatePreviewLevels()
    {
        var audioVolume = ResolveAudioVolumeAt(PlayheadSeconds);
        PreviewLevelsChanged?.Invoke(audioVolume);
    }

    private void RefreshClipLevelLines()
    {
        foreach (var audioClip in AudioClips)
        {
            UpdateAudioClipLevelLine(audioClip);
        }
    }

    private void UpdateAudioClipLevelLine(TimelineClipItem clip)
    {
        var drawableHeight = Math.Max(2, ClipVisualHeight - 2);
        var volumeLevel = Math.Clamp(clip.VolumeLevel, 0.0, 1.0);

        clip.AudioLevelLineTop = (1.0 - volumeLevel) * drawableHeight;
        clip.IsAudioLevelLineVisible = clip.VolumeLevel < 0.999;

        var waveformHeight = Math.Max(1.0, drawableHeight * volumeLevel);
        clip.AudioWaveformVisualHeight = waveformHeight;
        clip.AudioWaveformVisualTop = (drawableHeight - waveformHeight) / 2.0;
    }

    private double ResolveAudioVolumeAt(double seconds)
    {
        var activeClip = ResolveActiveAudioClipAt(seconds);
        if (activeClip is not null)
        {
            return activeClip.VolumeLevel;
        }

        var previewClip = ResolvePreviewClip();
        if (previewClip is null)
        {
            return 1.0;
        }

        var linkedAudio = ResolveActiveAudioClips().FirstOrDefault(clip =>
            string.Equals(clip.Path, previewClip.Path, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(clip.StartSeconds - previewClip.StartSeconds) < 0.001);

        return linkedAudio?.VolumeLevel ?? 1.0;
    }

    private VideoLaneItem? ResolvePrimaryVideoLane()
    {
        return VideoLanes.FirstOrDefault(lane => lane.IsPrimary) ?? VideoLanes.FirstOrDefault();
    }

    private VideoLaneItem? ResolveLaneByLabel(string? laneLabel)
    {
        if (string.IsNullOrWhiteSpace(laneLabel))
        {
            return null;
        }

        return VideoLanes.FirstOrDefault(lane => string.Equals(lane.Label, laneLabel, StringComparison.Ordinal));
    }

    private AudioLaneItem? ResolveAudioLaneByVideoLabel(string? videoLaneLabel)
    {
        var audioLaneLabel = MapVideoLaneLabelToAudioLaneLabel(videoLaneLabel);
        if (string.IsNullOrWhiteSpace(audioLaneLabel))
        {
            return AudioLanes.FirstOrDefault();
        }

        return AudioLanes.FirstOrDefault(lane => string.Equals(lane.Label, audioLaneLabel, StringComparison.Ordinal))
            ?? AudioLanes.FirstOrDefault();
    }

    private void RebuildAudioLaneCollections()
    {
        var laneStateByLabel = AudioLanes.ToDictionary(
            lane => lane.Label,
            lane => (lane.IsSolo, lane.IsMuted),
            StringComparer.Ordinal);

        AudioLanes.Clear();

        var projectedAudioLanes = VideoLanes
            .Select(lane => new { Label = MapVideoLaneLabelToAudioLaneLabel(lane.Label), lane.IsPrimary })
            .DistinctBy(x => x.Label)
            .ToList();

        var primaryAudioLane = projectedAudioLanes.FirstOrDefault(lane => lane.IsPrimary);
        if (primaryAudioLane is not null)
        {
            var primaryState = laneStateByLabel.TryGetValue(primaryAudioLane.Label, out var existingPrimaryState)
                ? existingPrimaryState
                : (false, false);
            AudioLanes.Add(new AudioLaneItem(primaryAudioLane.Label, true, primaryState.Item1, primaryState.Item2));
        }

        foreach (var projectedLane in projectedAudioLanes
                     .Where(lane => !lane.IsPrimary)
                     .OrderBy(lane => ResolveAudioLaneOrdinal(lane.Label))
                     .ThenBy(lane => lane.Label, StringComparer.Ordinal))
        {
            var state = laneStateByLabel.TryGetValue(projectedLane.Label, out var existingState)
                ? existingState
                : (false, false);
            AudioLanes.Add(new AudioLaneItem(projectedLane.Label, false, state.Item1, state.Item2));
        }

        if (AudioLanes.Count == 0)
        {
            AudioLanes.Add(new AudioLaneItem("AUDIO", true, false, false));
        }

        OnPropertyChanged(nameof(AudioLaneCount));
        OnPropertyChanged(nameof(TimelineCanvasHeight));
        OnPropertyChanged(nameof(PlayheadHeight));
    }

    private void RebuildAudioLaneClipCollections()
    {
        foreach (var lane in AudioLanes)
        {
            lane.Clips.Clear();
        }

        foreach (var clip in AudioClips)
        {
            var lane = ResolveAudioLaneByVideoLabel(clip.VideoLaneLabel);
            lane?.Clips.Add(clip);
        }
    }

    private static string MapVideoLaneLabelToAudioLaneLabel(string? videoLaneLabel)
    {
        if (string.IsNullOrWhiteSpace(videoLaneLabel))
        {
            return "AUDIO";
        }

        return videoLaneLabel.StartsWith("VIDEO", StringComparison.Ordinal)
            ? $"AUDIO{videoLaneLabel[5..]}"
            : $"AUDIO {videoLaneLabel}";
    }

    private static string BuildAudioClipKey(TimelineClipItem clip)
    {
        return $"{clip.Path}|{clip.StartSeconds:F3}|{clip.DurationSeconds:F3}|{clip.Name}|{clip.VideoLaneLabel}";
    }

    private static bool IsStillImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = System.IO.Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<TimelineClipItem> ResolveActiveAudioClips()
    {
        if (AudioClips.Count == 0)
        {
            return [];
        }

        var hasSoloLane = AudioLanes.Any(lane => lane.IsSolo);

        return AudioClips
            .Where(clip =>
            {
                var lane = ResolveAudioLaneByVideoLabel(clip.VideoLaneLabel);
                if (lane is null)
                {
                    return false;
                }

                if (lane.IsMuted)
                {
                    return false;
                }

                if (hasSoloLane && !lane.IsSolo)
                {
                    return false;
                }

                return true;
            })
            .ToList();
    }

    private TimelineClipItem? ResolveActiveAudioClipAt(double seconds)
    {
        if (AudioClips.Count == 0)
        {
            return null;
        }

        var hasSoloLane = AudioLanes.Any(lane => lane.IsSolo);
        TimelineClipItem? activeClip = null;

        foreach (var clip in AudioClips)
        {
            var lane = ResolveAudioLaneByVideoLabel(clip.VideoLaneLabel);
            if (lane is null)
            {
                continue;
            }

            if (lane.IsMuted)
            {
                continue;
            }

            if (hasSoloLane && !lane.IsSolo)
            {
                continue;
            }

            if (seconds < clip.StartSeconds || seconds > clip.StartSeconds + clip.DurationSeconds)
            {
                continue;
            }

            if (activeClip is null || clip.StartSeconds > activeClip.StartSeconds)
            {
                activeClip = clip;
            }
        }

        return activeClip;
    }

    private static int ResolveAudioLaneOrdinal(string laneLabel)
    {
        if (string.Equals(laneLabel, "AUDIO", StringComparison.Ordinal))
        {
            return 1;
        }

        var suffix = laneLabel.Replace("AUDIO", string.Empty, StringComparison.Ordinal).Trim();
        return int.TryParse(suffix, out var parsed) && parsed > 0 ? parsed : int.MaxValue;
    }

    private void RebuildLaneClipCollections()
    {
        foreach (var lane in VideoLanes)
        {
            lane.Clips.Clear();
        }

        var fallbackLane = ResolvePrimaryVideoLane();
        if (fallbackLane is null)
        {
            return;
        }

        foreach (var clip in VideoClips)
        {
            var lane = ResolveLaneByLabel(clip.VideoLaneLabel) ?? fallbackLane;
            if (!string.Equals(clip.VideoLaneLabel, lane.Label, StringComparison.Ordinal))
            {
                clip.VideoLaneLabel = lane.Label;
            }

            lane.Clips.Add(clip);
        }
    }

}

public sealed record TimelineMinorTick(bool ShowLine);

public sealed record TimelineMajorTick(string Label, double Width);

public sealed partial class VideoLaneItem : ObservableObject
{
    public VideoLaneItem(string label, bool isPrimary, bool isSolo, bool isHidden)
    {
        Label = label;
        IsPrimary = isPrimary;
        IsSolo = isSolo;
        IsHidden = isHidden;
    }

    public string Label { get; }

    public bool IsPrimary { get; }

    public ObservableCollection<TimelineClipItem> Clips { get; } = [];

    [ObservableProperty]
    private bool isSolo;

    [ObservableProperty]
    private bool isHidden;
}

public sealed partial class AudioLaneItem : ObservableObject
{
    public AudioLaneItem(string label, bool isPrimary, bool isSolo, bool isMuted)
    {
        Label = label;
        IsPrimary = isPrimary;
        IsSolo = isSolo;
        IsMuted = isMuted;
    }

    public string Label { get; }

    public bool IsPrimary { get; }

    public ObservableCollection<TimelineClipItem> Clips { get; } = [];

    [ObservableProperty]
    private bool isSolo;

    [ObservableProperty]
    private bool isMuted;
}

public sealed record PreviewVideoLayer(
    string Path,
    long PlaybackMilliseconds,
    bool DrawBlurredBackground,
    bool IsSelected,
    bool HasAnySelectedVideoClip,
    double TransformX,
    double TransformY,
    double TransformScale,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom);

public sealed record PreviewClipTransform(
    double TransformX,
    double TransformY,
    double TransformScale,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom)
{
    public static PreviewClipTransform Default { get; } = new(0, 0, 1, 0, 0, 0, 0);
}

public sealed record PreviewAudioState(string? Path, long PlaybackMilliseconds, double VolumeLevel, bool ShouldPlay)
{
    public static PreviewAudioState Silent { get; } = new(null, 0, 1.0, false);
}
