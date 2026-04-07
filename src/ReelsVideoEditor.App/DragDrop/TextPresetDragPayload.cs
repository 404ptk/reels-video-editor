using System;
using System.Globalization;
using ReelsVideoEditor.App.Models;

namespace ReelsVideoEditor.App.DragDrop;

public static class TextPresetDragPayload
{
    public const string Format = "application/x-reels-text-preset";

    public static string Build(TextPresetDefinition preset)
    {
        return $"{Uri.EscapeDataString(preset.Name)}|{Uri.EscapeDataString(preset.FontFamily)}|{preset.FontSize.ToString(CultureInfo.InvariantCulture)}|{Uri.EscapeDataString(preset.ColorHex)}|{Uri.EscapeDataString(preset.OutlineColorHex)}|{preset.OutlineThickness.ToString(CultureInfo.InvariantCulture)}|{preset.LineHeightMultiplier.ToString(CultureInfo.InvariantCulture)}|{preset.LetterSpacing.ToString(CultureInfo.InvariantCulture)}|{(preset.IsAutoCaptions ? "1" : "0")}|{Uri.EscapeDataString(Models.TextRevealEffect.Normalize(preset.TextRevealEffect))}";
    }

    public static bool TryParse(object? rawPayload, out TextPresetDefinition? preset)
    {
        preset = null;

        if (rawPayload is not string payload || string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload.Split('|');
        if (parts.Length != 4 && parts.Length != 6 && parts.Length != 7 && parts.Length != 9 && parts.Length != 10)
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
        var lineHeightMultiplier = 1d;
        var letterSpacing = 0d;
        var isAutoCaptions = false;
        var textRevealEffect = Models.TextRevealEffect.None;

        if (parts.Length >= 6)
        {
            outlineColorHex = Uri.UnescapeDataString(parts[4]);
            if (!double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineThickness))
            {
                return false;
            }

            outlineThickness = Math.Clamp(outlineThickness, 0, 24);
        }

        if (parts.Length >= 8)
        {
            if (!double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out lineHeightMultiplier)
                || !double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out letterSpacing))
            {
                return false;
            }

            lineHeightMultiplier = Math.Clamp(lineHeightMultiplier, 0.7, 2.5);
            letterSpacing = Math.Clamp(letterSpacing, 0, 20);
        }

        if (parts.Length == 7)
        {
            isAutoCaptions = parts[6] == "1";
        }
        else if (parts.Length == 9)
        {
            isAutoCaptions = parts[8] == "1";
        }
        else if (parts.Length == 10)
        {
            isAutoCaptions = parts[8] == "1";
            textRevealEffect = Models.TextRevealEffect.Normalize(Uri.UnescapeDataString(parts[9]));
        }

        // Fallback safety check in case the flag was missing but it's the exact UI preset name:
        if (!isAutoCaptions && name == ReelsVideoEditor.App.ViewModels.Text.TextViewModel.AutoCaptionsPresetName)
        {
            isAutoCaptions = true;
        }

        if (string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(fontFamily)
            || string.IsNullOrWhiteSpace(colorHex)
            || fontSize <= 0)
        {
            return false;
        }

        preset = new TextPresetDefinition(
            name,
            fontFamily,
            fontSize,
            colorHex,
            outlineColorHex,
            outlineThickness,
            lineHeightMultiplier,
            letterSpacing,
            textRevealEffect,
            isAutoCaptions);
        return true;
    }
}