using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ReelsVideoEditor.App.Services.Text;

namespace ReelsVideoEditor.App.ViewModels.Text;

public sealed partial class TextViewModel : ViewModelBase
{
    private static readonly Models.TextPresetDefinition[] DefaultPresets =
    [
        new("Sunset", "Inter", 14, "#FF6B6B"),
        new("Ocean", "Inter", 14, "#3A86FF"),
        new("Mint", "Inter", 14, "#2EC4B6")
    ];

    private bool isSyncingFromTimeline;
    private readonly TextPresetStorageService presetStorage = new();
    private readonly HashSet<string> builtInPresetNames = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> RenderableFonts { get; } = LoadRenderableFonts();

    public string Title { get; } = "Text";

    public string Description { get; } = "Drag the preset onto the timeline to add text to your video.";

    public string EditorTitle { get; } = "Edit text clip";

    public string EditorDescription { get; } = "Change text content, font, size and color for selected clip.";

    public ObservableCollection<Models.TextPresetDefinition> Presets { get; } = [];

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

    public IReadOnlyList<string> AvailableFonts { get; }

    public Action<string, string, double, string>? ApplySelectedTextSettingsRequested { get; set; }

    public TextViewModel()
    {
        AvailableFonts = RenderableFonts;
        LoadPresets();
    }

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
    private double selectedClipFontSize = 14;

    [ObservableProperty]
    private string selectedClipFontFamily = "Inter";

    [ObservableProperty]
    private string newPresetName = string.Empty;

    [ObservableProperty]
    private string presetSaveStatus = string.Empty;

    [ObservableProperty]
    private bool isEditingPreset;

    [ObservableProperty]
    private string editingPresetSourceName = string.Empty;

    [ObservableProperty]
    private string pendingDeletePresetName = string.Empty;

    public bool HasPresetSaveStatus => !string.IsNullOrWhiteSpace(PresetSaveStatus);

    public string SavePresetButtonLabel => IsEditingPreset ? "Update preset" : "Save preset";

    public string DeletePresetButtonLabel => IsDeletePendingForCurrentPreset ? "Confirm delete" : "Delete preset";

    public bool IsDeletePendingForCurrentPreset => IsEditingPreset && IsPendingDeletePreset(EditingPresetSourceName);

    private bool CanSavePreset => IsEditorVisible;

    private bool CanDeletePreset => IsEditingPreset && !string.IsNullOrWhiteSpace(EditingPresetSourceName);

    public IBrush SelectedColorPreviewBrush => new SolidColorBrush(Color.FromRgb(
        NormalizeColorChannel(SelectedColorR),
        NormalizeColorChannel(SelectedColorG),
        NormalizeColorChannel(SelectedColorB)));

    public string SelectedColorHex => $"#{NormalizeColorChannel(SelectedColorR):X2}{NormalizeColorChannel(SelectedColorG):X2}{NormalizeColorChannel(SelectedColorB):X2}";
}
