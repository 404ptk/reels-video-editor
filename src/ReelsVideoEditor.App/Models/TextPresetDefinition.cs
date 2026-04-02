using Avalonia.Media;

namespace ReelsVideoEditor.App.Models;

public sealed record TextPresetDefinition(
    string Name,
    string FontFamily,
    double FontSize,
    string ColorHex,
    string OutlineColorHex = "#000000",
    double OutlineThickness = 0,
    bool IsAutoCaptions = false)
{
    public string DisplayText { get; } = "Preview";

    public IBrush ColorBrush { get; } = Brush.Parse(ColorHex);

    public IBrush OutlineColorBrush { get; } = Brush.Parse(OutlineColorHex);

    public Avalonia.Media.FontFamily PreviewFontFamily { get; } = BuildPreviewFontFamily(FontFamily);

    private static Avalonia.Media.FontFamily BuildPreviewFontFamily(string fontFamily)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            try
            {
                return new Avalonia.Media.FontFamily(fontFamily.Trim());
            }
            catch
            {
                // Fall back to default font family for malformed values.
            }
        }

        return Avalonia.Media.FontFamily.Default;
    }
}