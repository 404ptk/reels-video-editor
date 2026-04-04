using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.ViewModels.Preview;

public sealed partial class PreviewTextOverlayLayer : ObservableObject
{
    private const double TextOverlayReferenceHeight = 1280.0;

    private double rawFontSize = 14;
    private double rawOutlineThickness;
    private double rawLineHeightMultiplier = 1.0;
    private double rawLetterSpacing;
    private double rawTransformScale = 1.0;
    private double rawCropLeft;
    private double rawCropTop;
    private double rawCropRight;
    private double rawCropBottom;

    [ObservableProperty]
    private string text = string.Empty;

    [ObservableProperty]
    private string colorHex = "#FFFFFF";

    [ObservableProperty]
    private string outlineColorHex = "#000000";

    [ObservableProperty]
    private FontFamily fontFamily = new("Inter");

    [ObservableProperty]
    private double scaledFontSize = 14;

    [ObservableProperty]
    private double scaledLineHeight = 16.8;

    [ObservableProperty]
    private double scaledLetterSpacing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutlineOffsetPx))]
    [NotifyPropertyChangedFor(nameof(OutlineOffsetNegPx))]
    [NotifyPropertyChangedFor(nameof(HasOutline))]
    private double scaledOutlineThickness;

    [ObservableProperty]
    private double transformX;

    [ObservableProperty]
    private double transformY;

    [ObservableProperty]
    private double transformScale = 1.0;

    [ObservableProperty]
    private double cropLeftPx;

    [ObservableProperty]
    private double cropTopPx;

    [ObservableProperty]
    private double cropWidth;

    [ObservableProperty]
    private double cropHeight;

    public double OutlineOffsetPx => ScaledOutlineThickness;

    public double OutlineOffsetNegPx => -ScaledOutlineThickness;

    public bool HasOutline => ScaledOutlineThickness > 0.01;

    public void Apply(TimelineTextOverlayLayer source, double frameWidth, double frameHeight)
    {
        Text = source.Text;
        ColorHex = source.ColorHex;
        OutlineColorHex = source.OutlineColorHex;
        FontFamily = ResolveFontFamily(source.FontFamily);
        TransformX = source.TransformX;
        TransformY = source.TransformY;

        rawFontSize = Math.Max(1.0, source.FontSize);
        rawOutlineThickness = Math.Clamp(source.OutlineThickness, 0, 24);
        rawLineHeightMultiplier = Math.Clamp(source.LineHeightMultiplier, 0.7, 2.5);
        rawLetterSpacing = Math.Clamp(source.LetterSpacing, 0, 20);
        rawTransformScale = Math.Max(0.1, source.TransformScale);
        rawCropLeft = Math.Clamp(source.CropLeft, 0.0, 0.95);
        rawCropTop = Math.Clamp(source.CropTop, 0.0, 0.95);
        rawCropRight = Math.Clamp(source.CropRight, 0.0, 0.95);
        rawCropBottom = Math.Clamp(source.CropBottom, 0.0, 0.95);

        RecomputeLayout(frameWidth, frameHeight);
    }

    public void RecomputeLayout(double frameWidth, double frameHeight)
    {
        var safeWidth = Math.Max(1.0, frameWidth);
        var safeHeight = Math.Max(1.0, frameHeight);
        var frameScale = safeHeight / TextOverlayReferenceHeight;

        TransformScale = rawTransformScale;
        ScaledFontSize = Math.Max(1.0, rawFontSize * frameScale);
        ScaledLineHeight = Math.Max(1.0, ScaledFontSize * 1.2 * rawLineHeightMultiplier);
        ScaledLetterSpacing = Math.Max(0.0, rawLetterSpacing * frameScale);
        ScaledOutlineThickness = Math.Max(0.0, rawOutlineThickness * frameScale);
        CropLeftPx = safeWidth * rawCropLeft;
        CropTopPx = safeHeight * rawCropTop;
        CropWidth = Math.Max(0.0, safeWidth * (1.0 - rawCropLeft - rawCropRight));
        CropHeight = Math.Max(0.0, safeHeight * (1.0 - rawCropTop - rawCropBottom));
    }

    private static FontFamily ResolveFontFamily(string fontFamily)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            try
            {
                return new FontFamily(fontFamily.Trim());
            }
            catch
            {
                // Fall back to a known-safe font family when incoming data is invalid.
            }
        }

        return new FontFamily("Inter");
    }
}
