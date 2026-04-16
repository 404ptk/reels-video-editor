using System;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace ReelsVideoEditor.App.Models;

public sealed record WatermarkPresetDefinition(
    string Name,
    string ImagePath,
    double Opacity,
    bool IsAddTile = false)
{
    public bool IsRegularTile => !IsAddTile;

    public string OpacityLabel => $"{Math.Round(Math.Clamp(Opacity, 0.0, 1.0) * 100.0, MidpointRounding.AwayFromZero):0}%";

    [JsonIgnore]
    public IImage? PreviewImage => Services.Watermarks.WatermarkPresetTilePreviewService.GetOrCreate(ImagePath);
}
