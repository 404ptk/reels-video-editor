using CommunityToolkit.Mvvm.ComponentModel;

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

    public TimelineClipItem(string name, string path, double startSeconds, double durationSeconds)
    {
        Name = name;
        Path = path;
        StartSeconds = startSeconds;
        DurationSeconds = durationSeconds;
    }
}
