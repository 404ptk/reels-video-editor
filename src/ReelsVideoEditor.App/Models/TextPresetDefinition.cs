using Avalonia.Media;
using System.Text.Json.Serialization;

namespace ReelsVideoEditor.App.Models;

public sealed partial record TextPresetDefinition(
    string Name,
    string FontFamily,
    double FontSize,
    string ColorHex,
    string OutlineColorHex = "#000000",
    double OutlineThickness = 0,
    double LineHeightMultiplier = 1.0,
    double LetterSpacing = 0,
    string TextRevealEffect = Models.TextRevealEffect.None,
    bool IsAutoCaptions = false,
    bool IsAddTile = false)
{
    public bool IsRegularTile => !IsAddTile;

    public string DisplayText { get; } = "Preview";

    public IBrush ColorBrush { get; } = Brush.Parse(ColorHex);

    public IBrush OutlineColorBrush { get; } = Brush.Parse(OutlineColorHex);

    public Avalonia.Media.FontFamily PreviewFontFamily { get; } = BuildPreviewFontFamily(FontFamily);

    [JsonIgnore]
    public IImage? PreviewImage => Services.Text.TextPresetTilePreviewService.GetOrCreate(this);

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