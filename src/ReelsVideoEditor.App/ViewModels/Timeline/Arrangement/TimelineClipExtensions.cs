using ReelsVideoEditor.App.Models;

namespace ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

public static class TimelineClipExtensions
{
    public static TextClipSettings GetTextSettings(this TimelineClipItem clip)
    {
        return new TextClipSettings(
            clip.Name,
            clip.TextContent,
            clip.TextColorHex,
            clip.TextOutlineColorHex,
            clip.TextOutlineThickness,
            clip.TextFontSize,
            clip.TextFontFamily,
            clip.TextLineHeightMultiplier,
            clip.TextLetterSpacing,
            clip.TextRevealEffect);
    }

    public static void ApplyTextSettings(this TimelineClipItem clip, TextClipSettings settings)
    {
        clip.Name = settings.Name;
        clip.TextContent = settings.TextContent;
        clip.TextColorHex = settings.TextColorHex;
        clip.TextOutlineColorHex = settings.TextOutlineColorHex;
        clip.TextOutlineThickness = settings.TextOutlineThickness;
        clip.TextFontSize = settings.TextFontSize;
        clip.TextFontFamily = settings.TextFontFamily;
        clip.TextLineHeightMultiplier = settings.TextLineHeightMultiplier;
        clip.TextLetterSpacing = settings.TextLetterSpacing;
        clip.TextRevealEffect = settings.TextRevealEffect;
    }
}
