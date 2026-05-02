using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using System;

namespace ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

public sealed partial class TimelineClipItem : ObservableObject
{
    public Guid LinkId { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string name;

    [ObservableProperty]
    private string path;

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
    private double fadeInDurationSeconds;

    [ObservableProperty]
    private double fadeOutDurationSeconds;

    [ObservableProperty]
    private double fadeInVisualWidth;

    [ObservableProperty]
    private double fadeOutVisualWidth;

    [ObservableProperty]
    private bool isFadeInVisualVisible;

    [ObservableProperty]
    private bool isFadeOutVisualVisible;

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

    partial void OnDurationSecondsChanged(double value)
    {
        UpdateFadeVisualMetrics();
    }

    partial void OnWidthChanged(double value)
    {
        UpdateFadeVisualMetrics();
    }

    partial void OnFadeInDurationSecondsChanged(double value)
    {
        UpdateFadeVisualMetrics();
    }

    partial void OnFadeOutDurationSecondsChanged(double value)
    {
        UpdateFadeVisualMetrics();
    }

    private void UpdateFadeVisualMetrics()
    {
        var safeDuration = Math.Max(0.0001, DurationSeconds);
        var safeWidth = Math.Max(0, Width);

        var inRatio = Math.Clamp(FadeInDurationSeconds / safeDuration, 0, 1);
        var outRatio = Math.Clamp(FadeOutDurationSeconds / safeDuration, 0, 1);

        FadeInVisualWidth = safeWidth * inRatio;
        FadeOutVisualWidth = safeWidth * outRatio;

        IsFadeInVisualVisible = FadeInVisualWidth >= 2;
        IsFadeOutVisualVisible = FadeOutVisualWidth >= 2;
    }

    public TimelineClipItem Clone(Guid? newLinkId = null)
    {
        return new TimelineClipItem(
            Name,
            Path,
            StartSeconds,
            DurationSeconds,
            newLinkId ?? LinkId,
            SourceStartSeconds,
            SourceDurationSeconds)
        {
            Left = Left,
            Width = Width,
            WaveformImage = WaveformImage,
            IsSelected = IsSelected,
            VideoLaneLabel = VideoLaneLabel,
            VolumeLevel = VolumeLevel,
            AudioLevelLineTop = AudioLevelLineTop,
            IsAudioLevelLineVisible = IsAudioLevelLineVisible,
            AudioWaveformVisualHeight = AudioWaveformVisualHeight,
            AudioWaveformVisualTop = AudioWaveformVisualTop,
            AudioWaveformVisualWidth = AudioWaveformVisualWidth,
            AudioWaveformVisualOffsetX = AudioWaveformVisualOffsetX,
            IsLeftTrimMarkerVisible = IsLeftTrimMarkerVisible,
            IsRightTrimMarkerVisible = IsRightTrimMarkerVisible,
            TextContent = TextContent,
            TextColorHex = TextColorHex,
            TextOutlineColorHex = TextOutlineColorHex,
            TextOutlineThickness = TextOutlineThickness,
            TextFontSize = TextFontSize,
            TextFontFamily = TextFontFamily,
            TextLineHeightMultiplier = TextLineHeightMultiplier,
            TextLetterSpacing = TextLetterSpacing,
            TextRevealEffect = TextRevealEffect,
            IsSubtitle = IsSubtitle,
            IsWatermark = IsWatermark,
            Opacity = Opacity,
            FadeInDurationSeconds = FadeInDurationSeconds,
            FadeOutDurationSeconds = FadeOutDurationSeconds,
            FadeInVisualWidth = FadeInVisualWidth,
            FadeOutVisualWidth = FadeOutVisualWidth,
            IsFadeInVisualVisible = IsFadeInVisualVisible,
            IsFadeOutVisualVisible = IsFadeOutVisualVisible,
            IsMediaMissing = IsMediaMissing,
            TransformX = TransformX,
            TransformY = TransformY,
            TransformScale = TransformScale,
            CropLeft = CropLeft,
            CropTop = CropTop,
            CropRight = CropRight,
            CropBottom = CropBottom
        };
    }
}
