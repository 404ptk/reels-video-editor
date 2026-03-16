using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System;

namespace ReelsVideoEditor.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const int TimelineDurationSeconds = 300;
    private const double BaseTickWidth = 14;
    private const int MinZoom = 25;
    private const int MaxZoom = 300;

    [ObservableProperty]
    private int zoomPercent = 100;

    public ObservableCollection<TimelineTick> Ticks { get; } = [];

    public double TickWidth => BaseTickWidth * ZoomPercent / 100.0;

    public double TimelineCanvasWidth => TickWidth * TimelineDurationSeconds;

    public MainWindowViewModel()
    {
        BuildTicks();
    }

    partial void OnZoomPercentChanged(int value)
    {
        OnPropertyChanged(nameof(TickWidth));
        OnPropertyChanged(nameof(TimelineCanvasWidth));
    }

    public void ChangeZoomFromWheel(double wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return;
        }

        var step = wheelDelta > 0 ? 10 : -10;
        var nextZoom = Math.Clamp(ZoomPercent + step, MinZoom, MaxZoom);

        if (nextZoom != ZoomPercent)
        {
            ZoomPercent = nextZoom;
        }
    }

    private void BuildTicks()
    {
        for (var second = 0; second <= TimelineDurationSeconds; second++)
        {
            var label = second % 5 == 0
                ? $"{second / 60:D2}:{second % 60:D2}"
                : string.Empty;

            Ticks.Add(new TimelineTick(label));
        }
    }
}

public sealed record TimelineTick(string Label);
