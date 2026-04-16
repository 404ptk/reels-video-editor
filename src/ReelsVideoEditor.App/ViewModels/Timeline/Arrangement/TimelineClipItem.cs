using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using System;

namespace ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

public sealed partial class TimelineClipItem : ObservableObject
{
    public Guid LinkId { get; }

    [ObservableProperty]
    private string name;

    public string Path { get; }

    [ObservableProperty]
    private double startSeconds;

    [ObservableProperty]
    private double durationSeconds;

    [ObservableProperty]
    private double sourceStartSeconds;

    [ObservableProperty]
    private double sourceDurationSeconds;

    [ObservableProperty]
    private double left;

    [ObservableProperty]
    private double width;

    [ObservableProperty]
    private IImage? waveformImage;

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
    private double audioWaveformVisualWidth = 1;

    [ObservableProperty]
    private double audioWaveformVisualOffsetX;

    [ObservableProperty]
    private bool isLeftTrimMarkerVisible;

    [ObservableProperty]
    private bool isRightTrimMarkerVisible;

    [ObservableProperty]
    private string textContent = "Preview";

    [ObservableProperty]
    private string textColorHex = "#FFFFFF";

    [ObservableProperty]
    private string textOutlineColorHex = "#000000";

    [ObservableProperty]
    private double textOutlineThickness;

    [ObservableProperty]
    private double textFontSize = 14;

    [ObservableProperty]
    private string textFontFamily = "Inter";

    [ObservableProperty]
    private double textLineHeightMultiplier = 1.0;

    [ObservableProperty]
    private double textLetterSpacing;

    [ObservableProperty]
    private string textRevealEffect = Models.TextRevealEffect.None;

    [ObservableProperty]
    private bool isSubtitle;

    [ObservableProperty]
    private bool isWatermark;

    [ObservableProperty]
    private double opacity = 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(ClipBackground))]
    [NotifyPropertyChangedFor(nameof(ClipBorderBrush))]
    [NotifyPropertyChangedFor(nameof(ClipTextBrush))]
    private bool isMediaMissing;

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

    public TimelineClipItem(
        string name,
        string path,
        double startSeconds,
        double durationSeconds,
        Guid? linkId = null,
        double sourceStartSeconds = 0,
        double sourceDurationSeconds = 0)
    {
        LinkId = linkId ?? Guid.NewGuid();
        Name = name;
        Path = path;
        StartSeconds = startSeconds;
        DurationSeconds = durationSeconds;
        SourceStartSeconds = Math.Max(0, sourceStartSeconds);
        SourceDurationSeconds = Math.Max(DurationSeconds, sourceDurationSeconds);
    }

    public string DisplayName => IsMediaMissing ? "Media Lost" : Name;

    public IBrush ClipBackground => IsMediaMissing
        ? new SolidColorBrush(Color.Parse("#AA9E2A2A"))
        : new SolidColorBrush(Color.Parse("#66C2D8C4"));

    public IBrush ClipBorderBrush => IsMediaMissing
        ? new SolidColorBrush(Color.Parse("#FFCF4A4A"))
        : new SolidColorBrush(Color.Parse("#33464A4F"));

    public IBrush ClipTextBrush => IsMediaMissing
        ? Brushes.White
        : new SolidColorBrush(Color.Parse("#222222"));

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }
}
