using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
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

    public ObservableCollection<TimelineMinorTick> MinorTicks { get; } = [];

    public ObservableCollection<TimelineMajorTick> MajorTicks { get; } = [];

    public ObservableCollection<TimelineClipItem> Clips { get; } = [];

    public double TickWidth => BaseTickWidth * ZoomPercent / 100.0;

    public double TimelineCanvasWidth => TickWidth * TimelineDurationSeconds;

    private static double TimelineBaseWidth => BaseTickWidth * TimelineDurationSeconds;

    public TimelineViewModel()
    {
        BuildMinorTicks();
        RebuildMajorTicks();
    }

    partial void OnZoomPercentChanged(int value)
    {
        OnPropertyChanged(nameof(TickWidth));
        OnPropertyChanged(nameof(TimelineCanvasWidth));
        RebuildMajorTicks();
        clipArrangementService.RebuildLayouts(Clips, TickWidth);
    }

    public void AddClipFromExplorer(string name, double durationSeconds, double dropX)
    {
        var clip = clipArrangementService.BuildClip(name, durationSeconds, dropX, TickWidth, TimelineDurationSeconds);
        Clips.Add(clip);
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

}

public sealed record TimelineMinorTick(bool ShowLine);

public sealed record TimelineMajorTick(string Label, double Width);
