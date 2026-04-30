namespace ReelsVideoEditor.App.Models;

public sealed record TextClipSettings(
    string Name,
    string TextContent,
    string TextColorHex,
    string TextOutlineColorHex,
    double TextOutlineThickness,
    double TextFontSize,
    string TextFontFamily,
    double TextLineHeightMultiplier,
    double TextLetterSpacing,
    string TextRevealEffect);
