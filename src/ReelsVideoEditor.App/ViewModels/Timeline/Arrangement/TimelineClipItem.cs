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
    private double opacityLevel = 1.0;

    [ObservableProperty]
    private double volumeLevel = 1.0;

    [ObservableProperty]
    private double videoLevelLineTop;

    [ObservableProperty]
    private bool isVideoLevelLineVisible;

    [ObservableProperty]
    private double audioLevelLineTop;

    [ObservableProperty]
    private bool isAudioLevelLineVisible;

    public TimelineClipItem(string name, string path, double startSeconds, double durationSeconds)
    {
        Name = name;
        Path = path;
        StartSeconds = startSeconds;
        DurationSeconds = durationSeconds;
    }
}
