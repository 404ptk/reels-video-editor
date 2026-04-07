using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace ReelsVideoEditor.App.ViewModels.Text;

public sealed partial class TextViewModel
{
    [RelayCommand]
    private void SelectBasicColor(Models.TextColorPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        ApplyColorFromHex(preset.ColorHex);
    }

    [RelayCommand]
    private void SelectBasicOutlineColor(Models.TextColorPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        ApplyOutlineColorFromHex(preset.ColorHex);
    }

    private void ApplyColorFromHex(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex) || !Color.TryParse(colorHex.Trim(), out var parsedColor))
        {
            parsedColor = Color.FromRgb(255, 255, 255);
        }

        SelectedColorR = parsedColor.R;
        SelectedColorG = parsedColor.G;
        SelectedColorB = parsedColor.B;
    }

    private static byte NormalizeColorChannel(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }

    private void ApplyOutlineColorFromHex(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex) || !Color.TryParse(colorHex.Trim(), out var parsedColor))
        {
            parsedColor = Color.FromRgb(0, 0, 0);
        }

        SelectedOutlineColorR = parsedColor.R;
        SelectedOutlineColorG = parsedColor.G;
        SelectedOutlineColorB = parsedColor.B;
    }

    private static string NormalizeHexColor(string? colorHex)
    {
        if (!string.IsNullOrWhiteSpace(colorHex) && Color.TryParse(colorHex.Trim(), out var parsedColor))
        {
            return $"#{parsedColor.R:X2}{parsedColor.G:X2}{parsedColor.B:X2}";
        }

        return "#FFFFFF";
    }

    private static string NormalizeOutlineHexColor(string? colorHex)
    {
        if (!string.IsNullOrWhiteSpace(colorHex) && Color.TryParse(colorHex.Trim(), out var parsedColor))
        {
            return $"#{parsedColor.R:X2}{parsedColor.G:X2}{parsedColor.B:X2}";
        }

        return "#000000";
    }

    private static double NormalizeOutlineThickness(double thickness)
    {
        return Math.Clamp(Math.Round(thickness, MidpointRounding.AwayFromZero), 0, 24);
    }

    private static double NormalizeLineHeightMultiplier(double multiplier)
    {
        return Math.Clamp(Math.Round(multiplier, 2, MidpointRounding.AwayFromZero), 0.7, 2.5);
    }

    private static double NormalizeLetterSpacing(double spacing)
    {
        return Math.Clamp(Math.Round(spacing, 1, MidpointRounding.AwayFromZero), 0, 20);
    }

    private static string NormalizeTextRevealEffect(string? textRevealEffect)
    {
        return Models.TextRevealEffect.Normalize(textRevealEffect);
    }
}
