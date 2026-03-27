using Avalonia.Media;

namespace ReelsVideoEditor.App.Models;

public sealed record TextPresetDefinition(
    string Name,
    string FontFamily,
    double FontSize,
    string ColorHex)
{
    public string DisplayText { get; } = "Preview";

    public IBrush ColorBrush { get; } = Brush.Parse(ColorHex);
}