using System;
using System.Globalization;
using ReelsVideoEditor.App.Models;

namespace ReelsVideoEditor.App.DragDrop;

public static class WatermarkPresetDragPayload
{
    public const string Format = "application/x-reels-watermark-preset";

    public static string Build(WatermarkPresetDefinition preset)
    {
        return $"{Uri.EscapeDataString(preset.Name)}|{Uri.EscapeDataString(preset.ImagePath)}|{preset.Opacity.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryParse(object? rawPayload, out WatermarkPresetDefinition? preset)
    {
        preset = null;

        if (rawPayload is not string payload || string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload.Split('|');
        if (parts.Length != 3)
        {
            return false;
        }

        var name = Uri.UnescapeDataString(parts[0]);
        var imagePath = Uri.UnescapeDataString(parts[1]);

        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(imagePath))
        {
            return false;
        }

        preset = new WatermarkPresetDefinition(name, imagePath, Math.Clamp(opacity, 0.0, 1.0));
        return true;
    }
}
