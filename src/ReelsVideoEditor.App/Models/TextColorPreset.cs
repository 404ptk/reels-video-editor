using Avalonia.Media;

namespace ReelsVideoEditor.App.Models;

public sealed record TextColorPreset(string Name, string ColorHex)
{
    public IBrush ColorBrush { get; } = Brush.Parse(ColorHex);
}