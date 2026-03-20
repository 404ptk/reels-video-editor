using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;

namespace ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

public sealed partial class TimelineClipItem : ObservableObject
{
    public string Name { get; }

    public string Path { get; }

    public double StartSeconds { get; }

    public double DurationSeconds { get; }

    [ObservableProperty]
    private double left;

    [ObservableProperty]
    private double width;

    [ObservableProperty]
    private Bitmap? waveformImage;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private double volumeLevel = 1.0;

    [ObservableProperty]
    private double audioLevelLineTop;

    [ObservableProperty]
    private bool isAudioLevelLineVisible;

    [ObservableProperty]
    private double audioWaveformVisualHeight = 1;

    [ObservableProperty]
    private double audioWaveformVisualTop;

    public TimelineClipItem(string name, string path, double startSeconds, double durationSeconds)
    {
        Name = name;
        Path = path;
        StartSeconds = startSeconds;
        DurationSeconds = durationSeconds;
    }
}
