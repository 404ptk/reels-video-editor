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

    private static string NormalizeHexColor(string? colorHex)
    {
        if (!string.IsNullOrWhiteSpace(colorHex) && Color.TryParse(colorHex.Trim(), out var parsedColor))
        {
            return $"#{parsedColor.R:X2}{parsedColor.G:X2}{parsedColor.B:X2}";
        }

        return "#FFFFFF";
    }
}
