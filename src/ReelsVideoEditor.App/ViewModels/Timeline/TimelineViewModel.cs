using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.ViewModels.Timeline;

public partial class TimelineViewModel : ViewModelBase
{
    private const int TimelineDurationSeconds = 300;
    private const double BaseTickWidth = 14;
    private static readonly int[] LabelIntervalsInSeconds = [1, 2, 5, 10, 15, 30, 60, 120, 300];
    private const int MinZoom = 25;
    private const int MaxZoom = 300;
    private readonly TimelineClipArrangementService clipArrangementService = new();

    [ObservableProperty]
    private int zoomPercent = 100;

    [ObservableProperty]
    private double playheadSeconds;

    [ObservableProperty]
    private bool isPlaybackActive;

    private long lastPlaybackMilliseconds = -1;
    private double playbackMaxSeconds = TimelineDurationSeconds;
    private bool hasPlaybackSession;

    public ObservableCollection<TimelineMinorTick> MinorTicks { get; } = [];

    public ObservableCollection<TimelineMajorTick> MajorTicks { get; } = [];

    public ObservableCollection<TimelineClipItem> Clips { get; } = [];

    public double TickWidth => BaseTickWidth * ZoomPercent / 100.0;

    public double TimelineCanvasWidth => TickWidth * TimelineDurationSeconds;

    public double PlayheadLeft => Math.Clamp(PlayheadSeconds, 0, TimelineDurationSeconds) * TickWidth;

    public double PlayheadVisualLeft => 10 + PlayheadLeft;

    public bool HasClips => Clips.Count > 0;

    public bool IsPlayheadVisible => true;

    private static double TimelineBaseWidth => BaseTickWidth * TimelineDurationSeconds;

    public TimelineViewModel()
    {
        Clips.CollectionChanged += OnClipsChanged;
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
        clipArrangementService.RebuildLayouts(Clips, TickWidth);
    }

    partial void OnPlayheadSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(PlayheadLeft));
        OnPropertyChanged(nameof(PlayheadVisualLeft));
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
        Clips.Add(clip);

        if (Clips.Count == 1)
        {
            PlayheadSeconds = clip.StartSeconds;
        }
    }

    public string? ResolvePreviewClipPath()
    {
        return Clips.FirstOrDefault()?.Path;
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
            PlayheadSeconds = 0;
            return;
        }

        playbackMaxSeconds = TimelineDurationSeconds;
        hasPlaybackSession = false;
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

    private void OnClipsChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        OnPropertyChanged(nameof(HasClips));

        if (Clips.Count == 0)
        {
            PlayheadSeconds = 0;
            hasPlaybackSession = false;
            return;
        }

        if (eventArgs.Action == NotifyCollectionChangedAction.Add && eventArgs.NewItems is { Count: > 0 })
        {
            return;
        }

        PlayheadSeconds = ResolvePreviewClipStartSeconds();
    }

    private double ResolvePreviewClipStartSeconds()
    {
        return Clips.FirstOrDefault()?.StartSeconds ?? 0;
    }

    private TimelineClipItem? ResolvePreviewClip()
    {
        return Clips.FirstOrDefault();
    }

}

public sealed record TimelineMinorTick(bool ShowLine);

public sealed record TimelineMajorTick(string Label, double Width);
