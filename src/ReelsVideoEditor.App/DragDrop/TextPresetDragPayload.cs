using System;
using System.Globalization;
using ReelsVideoEditor.App.Models;

namespace ReelsVideoEditor.App.DragDrop;

public static class TextPresetDragPayload
{
    public const string Format = "application/x-reels-text-preset";

    public static string Build(TextPresetDefinition preset)
    {
        return $"{Uri.EscapeDataString(preset.Name)}|{Uri.EscapeDataString(preset.FontFamily)}|{preset.FontSize.ToString(CultureInfo.InvariantCulture)}|{Uri.EscapeDataString(preset.ColorHex)}|{Uri.EscapeDataString(preset.OutlineColorHex)}|{preset.OutlineThickness.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryParse(object? rawPayload, out TextPresetDefinition? preset)
    {
        preset = null;

        if (rawPayload is not string payload || string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload.Split('|');
        if (parts.Length != 4 && parts.Length != 6)
        {
            return false;
        }

        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var fontSize))
        {
            return false;
        }

        var name = Uri.UnescapeDataString(parts[0]);
        var fontFamily = Uri.UnescapeDataString(parts[1]);
        var colorHex = Uri.UnescapeDataString(parts[3]);
        var outlineColorHex = "#000000";
        var outlineThickness = 0d;
        if (parts.Length == 6)
        {
            outlineColorHex = Uri.UnescapeDataString(parts[4]);
            if (!double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineThickness))
            {
                return false;
            }

            outlineThickness = Math.Clamp(outlineThickness, 0, 24);
        }
        if (string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(fontFamily)
            || string.IsNullOrWhiteSpace(colorHex)
            || fontSize <= 0)
        {
            return false;
        }

        preset = new TextPresetDefinition(name, fontFamily, fontSize, colorHex, outlineColorHex, outlineThickness);
        return true;
    }
}