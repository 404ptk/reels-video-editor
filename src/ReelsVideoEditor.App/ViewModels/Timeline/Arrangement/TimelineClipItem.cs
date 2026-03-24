using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;
using System;

namespace ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

public sealed partial class TimelineClipItem : ObservableObject
{
    public Guid LinkId { get; }

    public string Name { get; }

    public string Path { get; }

    [ObservableProperty]
    private double startSeconds;

    [ObservableProperty]
    private double durationSeconds;

    [ObservableProperty]
    private double left;

    [ObservableProperty]
    private double width;

    [ObservableProperty]
    private Bitmap? waveformImage;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private string videoLaneLabel = string.Empty;

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

    [ObservableProperty]
    private double transformX;

    [ObservableProperty]
    private double transformY;

    [ObservableProperty]
    private double transformScale = 1.0;

    [ObservableProperty]
    private double cropLeft;

    [ObservableProperty]
    private double cropTop;

    [ObservableProperty]
    private double cropRight;

    [ObservableProperty]
    private double cropBottom;

    public TimelineClipItem(string name, string path, double startSeconds, double durationSeconds, Guid? linkId = null)
    {
        LinkId = linkId ?? Guid.NewGuid();
        Name = name;
        Path = path;
        StartSeconds = startSeconds;
        DurationSeconds = durationSeconds;
    }
}
