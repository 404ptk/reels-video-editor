using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace ReelsVideoEditor.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const int TimelineDurationSeconds = 300;
    private const double BaseTickWidth = 14;

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
