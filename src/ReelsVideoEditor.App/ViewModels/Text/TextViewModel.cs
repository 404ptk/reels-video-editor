using System;
using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.ViewModels.Text;

public sealed partial class TextViewModel : ViewModelBase
{
    private bool isSyncingFromTimeline;

    public string Title { get; } = "Text";

    public string Description { get; } = "Drag the preset onto the timeline to add text to your video.";

    public string EditorTitle { get; } = "Edit text clip";

    public string EditorDescription { get; } = "Change text content, size and color for selected clip.";

    public IReadOnlyList<Models.TextPresetDefinition> Presets { get; } =
    [
        new("Sunset", "Inter", 56, "#FF6B6B"),
        new("Ocean", "Inter", 56, "#3A86FF"),
        new("Mint", "Inter", 56, "#2EC4B6")
    ];

    public IReadOnlyList<Models.TextColorPreset> BasicColors { get; } =
    [
        new("White", "#FFFFFF"),
        new("Black", "#121212"),
        new("Red", "#E63946"),
        new("Orange", "#F77F00"),
        new("Yellow", "#F4D35E"),
        new("Green", "#2A9D8F"),
        new("Cyan", "#00BCD4"),
        new("Blue", "#3A86FF"),
        new("Purple", "#9B5DE5"),
        new("Pink", "#FF5D8F")
    ];

    public Action<string, string, double>? ApplySelectedTextSettingsRequested { get; set; }

    [ObservableProperty]
    private bool isEditorVisible;

    public bool IsPresetVisible => !IsEditorVisible;

    [ObservableProperty]
    private bool hasSelectedTextClip;

    [ObservableProperty]
    private string selectedClipText = "Preview";

    [ObservableProperty]
    private double selectedColorR = 255;

    [ObservableProperty]
    private double selectedColorG = 255;

    [ObservableProperty]
    private double selectedColorB = 255;

    [ObservableProperty]
    private double selectedClipFontSize = 56;

    public IBrush SelectedColorPreviewBrush => new SolidColorBrush(Color.FromRgb(
        NormalizeColorChannel(SelectedColorR),
        NormalizeColorChannel(SelectedColorG),
        NormalizeColorChannel(SelectedColorB)));

    public string SelectedColorHex => $"#{NormalizeColorChannel(SelectedColorR):X2}{NormalizeColorChannel(SelectedColorG):X2}{NormalizeColorChannel(SelectedColorB):X2}";

    public void SyncSelectedTextClip(TimelineSelectedTextClipState state)
    {
        isSyncingFromTimeline = true;
        try
        {
            HasSelectedTextClip = state.HasSelection;
            if (state.HasSelection)
            {
                SelectedClipText = state.Text;
                ApplyColorFromHex(state.ColorHex);
                SelectedClipFontSize = state.FontSize;
                IsEditorVisible = true;
            }
            else
            {
                IsEditorVisible = false;
            }
        }
        finally
        {
            isSyncingFromTimeline = false;
        }
    }

    [RelayCommand]
    private void BackToPresets()
    {
        IsEditorVisible = false;
    }

    partial void OnIsEditorVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPresetVisible));
    }

    partial void OnSelectedClipTextChanged(string value)
    {
        ApplySelectedTextSettings();
    }

    partial void OnSelectedColorRChanged(double value)
    {
        OnPropertyChanged(nameof(SelectedColorPreviewBrush));
        OnPropertyChanged(nameof(SelectedColorHex));
        ApplySelectedTextSettings();
    }

    partial void OnSelectedColorGChanged(double value)
    {
        OnPropertyChanged(nameof(SelectedColorPreviewBrush));
        OnPropertyChanged(nameof(SelectedColorHex));
        ApplySelectedTextSettings();
    }

    partial void OnSelectedColorBChanged(double value)
    {
        OnPropertyChanged(nameof(SelectedColorPreviewBrush));
        OnPropertyChanged(nameof(SelectedColorHex));
        ApplySelectedTextSettings();
    }

    partial void OnSelectedClipFontSizeChanged(double value)
    {
        ApplySelectedTextSettings();
    }

    private void ApplySelectedTextSettings()
    {
        if (isSyncingFromTimeline || !HasSelectedTextClip || !IsEditorVisible)
        {
            return;
        }

        ApplySelectedTextSettingsRequested?.Invoke(SelectedClipText, SelectedColorHex, SelectedClipFontSize);
    }

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
}
