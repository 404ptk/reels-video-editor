using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    private readonly TimelineClipArrangementService clipArrangementService = new();
    private readonly TimelineWaveformRenderService waveformRenderService = new();
    
    private readonly Stack<Action> undoStack = new();

    [ObservableProperty]
    private int zoomPercent = 100;

    [ObservableProperty]
    private bool isVideoSolo;

    [ObservableProperty]
    private bool isVideoHidden;

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
    private bool hasPlaybackSession;

    public Action<double>? PlayheadSeekRequested { get; set; }

    public Action? PreviewClipChanged { get; set; }

    public Action<double, double>? PreviewLevelsChanged { get; set; }

    public ObservableCollection<TimelineMinorTick> MinorTicks { get; } = [];

    public ObservableCollection<TimelineMajorTick> MajorTicks { get; } = [];

    public ObservableCollection<TimelineClipItem> VideoClips { get; } = [];

    public ObservableCollection<TimelineClipItem> AudioClips { get; } = [];

    public ObservableCollection<TimelineClipItem> Clips => VideoClips;

    public double TickWidth => BaseTickWidth * ZoomPercent / 100.0;

    public double TimelineCanvasWidth => TickWidth * TimelineDurationSeconds;

    public double LaneContainerHeight => LaneHeaderHeight + LaneContentHeight;

    public double ClipVisualHeight => Math.Max(18, LaneContainerHeight - LaneClipVerticalPadding);

    public double TimelineCanvasHeight => TickSectionHeight + TrackTopSpacing + LaneContainerHeight + TrackGap + LaneContainerHeight;

    public double PlayheadHeight => TimelineCanvasHeight;

    public double PlayheadLeft => Math.Clamp(PlayheadSeconds, 0, TimelineDurationSeconds) * TickWidth;

    public double PlayheadVisualLeft => 10 + PlayheadLeft;

    public bool HasClips => VideoClips.Count > 0;

    public bool IsPlayheadVisible => true;

    private static double TimelineBaseWidth => BaseTickWidth * TimelineDurationSeconds;

    public TimelineViewModel()
    {
        VideoClips.CollectionChanged += OnVideoClipsChanged;
        BuildMinorTicks();
        RebuildMajorTicks();
    }

    partial void OnZoomPercentChanged(int value)
    {
        OnPropertyChanged(nameof(TickWidth));
        OnPropertyChanged(nameof(TimelineCanvasWidth));
        OnPropertyChanged(nameof(PlayheadLeft));
        OnPropertyChanged(nameof(PlayheadVisualLeft));
        RebuildMajorTicks();
        clipArrangementService.RebuildLayouts(VideoClips, TickWidth);
        clipArrangementService.RebuildLayouts(AudioClips, TickWidth);
    }

    partial void OnPlayheadSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(PlayheadLeft));
        OnPropertyChanged(nameof(PlayheadVisualLeft));
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

    partial void OnIsPlaybackActiveChanged(bool value)
    {
        if (!value)
        {
            hasPlaybackSession = false;
        }
    }

    public void AddClipFromExplorer(string name, string path, double durationSeconds, double dropX)
    {
        var clip = clipArrangementService.BuildClip(name, path, durationSeconds, dropX, TickWidth, TimelineDurationSeconds);
        VideoClips.Add(clip);

        var linkedAudio = clipArrangementService.BuildLinkedAudioClip(clip);
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
        return VideoClips.FirstOrDefault()?.Path;
    }

    public void UpdatePlayheadFromPlayback(long playbackMilliseconds)
    {
        if (!IsPlaybackActive)
        {
            return;
        }

        if (!hasPlaybackSession)
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
        PlayheadSeconds = Math.Clamp(clampedSeconds, 0, TimelineDurationSeconds);
    }

    public void SeekToPosition(double pointerX)
    {
        var playheadSeconds = Math.Max(0, (pointerX - 10) / TickWidth);
        var clampedSeconds = Math.Clamp(playheadSeconds, 0, TimelineDurationSeconds);

        PlayheadSeconds = clampedSeconds;
        lastPlaybackMilliseconds = -1;
        PlayheadSeekRequested?.Invoke(clampedSeconds);
    }

    public void SetPlaybackActive(bool isPlaying)
    {
        IsPlaybackActive = isPlaying;
        lastPlaybackMilliseconds = -1;

        if (!isPlaying)
        {
            return;
        }

        var activeClip = ResolvePreviewClip();
        if (activeClip is not null)
        {
            playbackMaxSeconds = Math.Max(0.01, activeClip.DurationSeconds);
            hasPlaybackSession = true;
            return;
        }

        playbackMaxSeconds = TimelineDurationSeconds;
        hasPlaybackSession = false;
    }

    public void RefreshPreviewLevels()
    {
        UpdatePreviewLevels();
    }

    public void SetVideoClipOpacity(TimelineClipItem clip, double opacityLevel)
    {
        clip.OpacityLevel = Math.Clamp(opacityLevel, 0.0, 1.0);
        UpdateVideoClipLevelLine(clip);
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
        OnPropertyChanged(nameof(HasClips));
        PreviewClipChanged?.Invoke();

        if (VideoClips.Count == 0)
        {
            AudioClips.Clear();
            PlayheadSeconds = 0;
            hasPlaybackSession = false;
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
            var audioClip = clipArrangementService.BuildLinkedAudioClip(videoClip);

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
        var waveform = await waveformRenderService.TryRenderWaveformAsync(audioClip.Path);
        if (waveform is null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => audioClip.WaveformImage = waveform);
    }

    private double ResolvePreviewClipStartSeconds()
    {
        return VideoClips.FirstOrDefault()?.StartSeconds ?? 0;
    }

    private TimelineClipItem? ResolvePreviewClip()
    {
        return VideoClips.FirstOrDefault();
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
                
                var linkedAudio = clipArrangementService.BuildLinkedAudioClip(clip);
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
        var videoOpacity = ResolveVideoOpacityAt(PlayheadSeconds);
        var audioVolume = ResolveAudioVolumeAt(PlayheadSeconds);
        PreviewLevelsChanged?.Invoke(videoOpacity, audioVolume);
    }

    private void RefreshClipLevelLines()
    {
        foreach (var videoClip in VideoClips)
        {
            UpdateVideoClipLevelLine(videoClip);
        }

        foreach (var audioClip in AudioClips)
        {
            UpdateAudioClipLevelLine(audioClip);
        }
    }

    private void UpdateVideoClipLevelLine(TimelineClipItem clip)
    {
        var drawableHeight = Math.Max(0, ClipVisualHeight - 2);
        clip.VideoLevelLineTop = (1.0 - Math.Clamp(clip.OpacityLevel, 0.0, 1.0)) * drawableHeight;
        clip.IsVideoLevelLineVisible = clip.OpacityLevel < 0.999;
    }

    private void UpdateAudioClipLevelLine(TimelineClipItem clip)
    {
        var drawableHeight = Math.Max(0, ClipVisualHeight - 2);
        clip.AudioLevelLineTop = (1.0 - Math.Clamp(clip.VolumeLevel, 0.0, 1.0)) * drawableHeight;
        clip.IsAudioLevelLineVisible = clip.VolumeLevel < 0.999;
    }

    private double ResolveVideoOpacityAt(double seconds)
    {
        var activeClip = VideoClips.FirstOrDefault(clip => seconds >= clip.StartSeconds && seconds <= clip.StartSeconds + clip.DurationSeconds);
        if (activeClip is not null)
        {
            return activeClip.OpacityLevel;
        }

        var previewClip = ResolvePreviewClip();
        return previewClip?.OpacityLevel ?? 1.0;
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

}

public sealed record TimelineMinorTick(bool ShowLine);

public sealed record TimelineMajorTick(string Label, double Width);
