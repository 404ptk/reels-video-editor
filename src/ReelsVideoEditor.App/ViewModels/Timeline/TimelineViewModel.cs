using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
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

    public Action<double>? PreviewLevelsChanged { get; set; }

    public ObservableCollection<TimelineMinorTick> MinorTicks { get; } = [];

    public ObservableCollection<TimelineMajorTick> MajorTicks { get; } = [];

    public ObservableCollection<TimelineClipItem> VideoClips { get; } = [];

    public ObservableCollection<TimelineClipItem> AudioClips { get; } = [];

    public ObservableCollection<VideoLaneItem> VideoLanes { get; } = [new("VIDEO", true, false, false)];

    public ObservableCollection<TimelineClipItem> Clips => VideoClips;

    public double TickWidth => BaseTickWidth * ZoomPercent / 100.0;

    public double TimelineCanvasWidth => TickWidth * TimelineDurationSeconds;

    public double LaneContainerHeight => LaneHeaderHeight + LaneContentHeight;

    public double ClipVisualHeight => Math.Max(18, LaneContainerHeight - LaneClipVerticalPadding);

    public int VideoLaneCount => VideoLanes.Count;

    public bool CanAddNewLine => VideoLaneCount < MaxVideoLines;

    // Compatibility surface used by preview/export; maps to primary lane state.
    public bool IsVideoSolo => VideoLanes.FirstOrDefault()?.IsSolo ?? false;

    public bool IsVideoHidden => VideoLanes.FirstOrDefault()?.IsHidden ?? false;

    public double TimelineCanvasHeight => TickSectionHeight + TrackTopSpacing + (VideoLaneCount * LaneContainerHeight) + (VideoLaneCount * TrackGap) + LaneContainerHeight;

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
        foreach (var lane in VideoLanes)
        {
            lane.PropertyChanged += OnVideoLanePropertyChanged;
        }
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
        OnPropertyChanged(nameof(CanAddNewLine));
        OnPropertyChanged(nameof(TimelineCanvasHeight));
        OnPropertyChanged(nameof(PlayheadHeight));
        OnPropertyChanged(nameof(IsVideoSolo));
        OnPropertyChanged(nameof(IsVideoHidden));
        RebuildLaneClipCollections();
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

    partial void OnIsPlaybackActiveChanged(bool value)
    {
    }

    public void AddClipFromExplorer(string name, string path, double durationSeconds, double dropX, string? targetLaneLabel = null)
    {
        var clip = TimelineClipArrangementService.BuildClip(name, path, durationSeconds, dropX, TickWidth, TimelineDurationSeconds);
        var targetLane = ResolveLaneByLabel(targetLaneLabel) ?? ResolvePrimaryVideoLane();
        clip.VideoLaneLabel = targetLane?.Label ?? string.Empty;
        VideoClips.Add(clip);

        var linkedAudio = TimelineClipArrangementService.BuildLinkedAudioClip(clip);
        AudioClips.Add(linkedAudio);
        _ = LoadAudioWaveformAsync(linkedAudio);

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
        var activeClips = ResolveVisibleVideoClips()
            .Where(clip =>
                timelineSeconds >= clip.StartSeconds
                && timelineSeconds <= clip.StartSeconds + clip.DurationSeconds)
            .OrderByDescending(clip => ResolveLaneLayerIndex(clip.VideoLaneLabel))
            .ThenBy(clip => clip.StartSeconds)
            .ToList();

        if (activeClips.Count == 0)
        {
            return [];
        }

        var result = new List<PreviewVideoLayer>(activeClips.Count);
        for (var i = 0; i < activeClips.Count; i++)
        {
            var clip = activeClips[i];
            var localSeconds = Math.Clamp(timelineSeconds - clip.StartSeconds, 0, clip.DurationSeconds);
            var localMilliseconds = (long)Math.Round(localSeconds * 1000, MidpointRounding.AwayFromZero);

            // Only the bottom-most visible layer should paint the blurred fill.
            result.Add(new PreviewVideoLayer(clip.Path, localMilliseconds, DrawBlurredBackground: i == 0));
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
        var activeAudio = AudioClips
            .OrderByDescending(clip => clip.StartSeconds)
            .FirstOrDefault(clip =>
                timelineSeconds >= clip.StartSeconds
                && timelineSeconds <= clip.StartSeconds + clip.DurationSeconds);
        if (activeAudio is null)
        {
            return PreviewAudioState.Silent;
        }

        var localSeconds = Math.Clamp(timelineSeconds - activeAudio.StartSeconds, 0, activeAudio.DurationSeconds);
        var localMilliseconds = (long)Math.Round(localSeconds * 1000, MidpointRounding.AwayFromZero);
        var volume = Math.Clamp(activeAudio.VolumeLevel, 0.0, 1.0);

        return new PreviewAudioState(activeAudio.Path, localMilliseconds, volume, ShouldPlay: true);
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
            RebuildLaneClipCollections();
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
            TimelineClipArrangementService.RebuildLayouts([linkedAudio], TickWidth);
        }

        if (laneChanged)
        {
            RebuildLaneClipCollections();
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
            PlayheadSeconds = 0;
            RefreshClipLevelLines();
            UpdatePreviewLevels();
            return;
        }

        if (eventArgs.Action == NotifyCollectionChangedAction.Add && eventArgs.NewItems is { Count: > 0 })
        {
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
            clip => $"{clip.Path}|{clip.StartSeconds:F3}|{clip.DurationSeconds:F3}|{clip.Name}",
            clip => clip.VolumeLevel);

        AudioClips.Clear();

        foreach (var videoClip in VideoClips)
        {
            var audioClip = TimelineClipArrangementService.BuildLinkedAudioClip(videoClip);

            var key = $"{audioClip.Path}|{audioClip.StartSeconds:F3}|{audioClip.DurationSeconds:F3}|{audioClip.Name}";
            if (volumeByKey.TryGetValue(key, out var volumeLevel))
            {
                audioClip.VolumeLevel = volumeLevel;
            }

            AudioClips.Add(audioClip);
            _ = LoadAudioWaveformAsync(audioClip);
        }

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

        var visibleClips = ResolveVisibleVideoClips().ToList();
        if (visibleClips.Count == 0)
        {
            return null;
        }

        var playheadMatch = visibleClips
            .OrderBy(clip => ResolveLaneLayerIndex(clip.VideoLaneLabel))
            .ThenByDescending(clip => clip.StartSeconds)
            .FirstOrDefault(clip =>
                PlayheadSeconds >= clip.StartSeconds
                && PlayheadSeconds <= clip.StartSeconds + clip.DurationSeconds);
        if (playheadMatch is not null)
        {
            return playheadMatch;
        }

        return visibleClips
            .OrderBy(clip => ResolveLaneLayerIndex(clip.VideoLaneLabel))
            .ThenBy(clip => clip.StartSeconds)
            .FirstOrDefault();
    }

    private IEnumerable<TimelineClipItem> ResolveVisibleVideoClips()
    {
        var lanesByLabel = VideoLanes.ToDictionary(lane => lane.Label, lane => lane, StringComparer.Ordinal);
        var hasSoloLanes = VideoLanes.Any(lane => lane.IsSolo);

        foreach (var clip in VideoClips)
        {
            var lane = lanesByLabel.TryGetValue(clip.VideoLaneLabel, out var resolvedLane)
                ? resolvedLane
                : ResolvePrimaryVideoLane();
            if (lane is null)
            {
                continue;
            }

            if (lane.IsHidden)
            {
                continue;
            }

            if (hasSoloLanes && !lane.IsSolo)
            {
                continue;
            }

            yield return clip;
        }
    }

    private int ResolveLaneLayerIndex(string laneLabel)
    {
        for (var i = 0; i < VideoLanes.Count; i++)
        {
            if (string.Equals(VideoLanes[i].Label, laneLabel, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return VideoLanes.Count;
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
        var maxVideoSeconds = ResolveVisibleVideoClips()
            .Select(clip => clip.StartSeconds + clip.DurationSeconds)
            .DefaultIfEmpty(0)
            .Max();
        var maxAudioSeconds = IsAudioMuted
            ? 0
            : AudioClips
                .Select(clip => clip.StartSeconds + clip.DurationSeconds)
                .DefaultIfEmpty(0)
                .Max();
        var resolved = Math.Max(maxVideoSeconds, maxAudioSeconds);
        return Math.Clamp(resolved, 0.01, TimelineDurationSeconds);
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

        foreach (var clip in VideoClips)
        {
            clip.IsSelected = (clip.Left < maxX && (clip.Left + clip.Width) > minX);
        }
        foreach (var clip in AudioClips)
        {
            clip.IsSelected = (clip.Left < maxX && (clip.Left + clip.Width) > minX);
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
        foreach (var clip in VideoClips) clip.IsSelected = false;
        foreach (var clip in AudioClips) clip.IsSelected = false;
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
        var activeClip = AudioClips.FirstOrDefault(clip => seconds >= clip.StartSeconds && seconds <= clip.StartSeconds + clip.DurationSeconds);
        if (activeClip is not null)
        {
            return activeClip.VolumeLevel;
        }

        var previewClip = ResolvePreviewClip();
        if (previewClip is null)
        {
            return 1.0;
        }

        var linkedAudio = AudioClips.FirstOrDefault(clip =>
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

public sealed record PreviewVideoLayer(string Path, long PlaybackMilliseconds, bool DrawBlurredBackground);

public sealed record PreviewAudioState(string? Path, long PlaybackMilliseconds, double VolumeLevel, bool ShouldPlay)
{
    public static PreviewAudioState Silent { get; } = new(null, 0, 1.0, false);
}
