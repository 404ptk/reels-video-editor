using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ReelsVideoEditor.App.Services.Text;

namespace ReelsVideoEditor.App.ViewModels.Text;

public sealed partial class TextViewModel : ViewModelBase
{
    public const string AutoCaptionsPresetName = "🎤 Auto Captions";

    private static readonly Models.TextPresetDefinition[] DefaultTextPresets =
    [
        new("Sunset", "Inter", 14, "#FF6B6B"),
        new("Ocean", "Inter", 14, "#3A86FF"),
        new("Mint", "Inter", 14, "#2EC4B6")
    ];

    private static readonly Models.TextPresetDefinition[] DefaultSubtitlesPresets =
    [
        new(AutoCaptionsPresetName, "Inter", 18, "#FFFFFF", "#000000", 3, TextRevealEffect: Models.TextRevealEffect.Pop, IsAutoCaptions: true),
    ];

    private bool isSyncingFromTimeline;
    private readonly TextPresetStorageService presetStorage;
    private readonly IReadOnlyList<Models.TextPresetDefinition> defaultPresets;
    private readonly bool autoCaptionsPresetMode;
    private readonly string title;
    private readonly string description;
    private readonly HashSet<string> builtInPresetNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Models.TextPresetDefinition AddPresetTile = new(
        "+",
        "Inter",
        14,
        "#2EC4B6",
        IsAddTile: true);
    private IReadOnlyList<string> RenderableFonts { get; } = LoadRenderableFonts();

    public string Title => title;

    public string Description => description;

    public string EditorTitle { get; } = "Edit text clip";

    public string EditorDescription { get; } = "Change text content, font, size and color for selected clip.";

    public ObservableCollection<Models.TextPresetDefinition> Presets { get; } = [];

    public IEnumerable<Models.TextPresetDefinition> PresetTiles => Presets.Concat([AddPresetTile]);

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

    public IReadOnlyList<string> AvailableTextRevealEffects { get; } =
    [
        Models.TextRevealEffect.None,
        Models.TextRevealEffect.Pop
    ];

    public bool IsSubtitlesMode => autoCaptionsPresetMode;

    public Action<string, string, double, string, string, double, double, double, string>? ApplySelectedTextSettingsRequested { get; set; }

    public Func<Models.TextPresetDefinition, double, string?, System.Threading.Tasks.Task>? AutoCaptionsRequested { get; set; }

    public static TextViewModel CreateTextPanel()
    {
        return new TextViewModel(autoCaptionsPresetMode: false);
    }

    public static TextViewModel CreateSubtitlesPanel()
    {
        return new TextViewModel(autoCaptionsPresetMode: true);
    }

    public TextViewModel()
        : this(autoCaptionsPresetMode: false)
    {
    }

    private TextViewModel(bool autoCaptionsPresetMode)
    {
        this.autoCaptionsPresetMode = autoCaptionsPresetMode;
        title = autoCaptionsPresetMode ? "Subtitles" : "Text";
        description = autoCaptionsPresetMode
            ? "Drag an auto-captions preset onto the timeline to generate subtitles."
            : "Drag the preset onto the timeline to add text to your video.";

        defaultPresets = autoCaptionsPresetMode ? DefaultSubtitlesPresets : DefaultTextPresets;
        presetStorage = CreatePresetStorageService(autoCaptionsPresetMode);
        AvailableFonts = RenderableFonts;
        SelectedClipTextRevealEffect = autoCaptionsPresetMode ? Models.TextRevealEffect.Pop : Models.TextRevealEffect.None;
        Presets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(PresetTiles));
        LoadPresets();
    }

    private static TextPresetStorageService CreatePresetStorageService(bool subtitlesMode)
    {
        if (!subtitlesMode)
        {
            return new TextPresetStorageService();
        }

        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        var filePath = Path.Combine(baseDirectory, "ReelsVideoEditor", "subtitles-presets.json");
        return new TextPresetStorageService(filePath);
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
    private double selectedOutlineColorR;

    [ObservableProperty]
    private double selectedOutlineColorG;

    [ObservableProperty]
    private double selectedOutlineColorB;

    [ObservableProperty]
    private double selectedClipFontSize = 14;

    [ObservableProperty]
    private string selectedClipFontFamily = "Inter";

    [ObservableProperty]
    private double selectedClipOutlineThickness;

    [ObservableProperty]
    private double selectedClipLineHeightMultiplier = 1.0;

    [ObservableProperty]
    private double selectedClipLetterSpacing;

    [ObservableProperty]
    private string selectedClipTextRevealEffect = Models.TextRevealEffect.None;

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

    [ObservableProperty]
    private bool isTranscribing;

    [ObservableProperty]
    private bool isApplyingSubtitles;

    [ObservableProperty]
    private double transcriptionProgress;

    [ObservableProperty]
    private string transcriptionStatus = string.Empty;

    public bool IsSubtitlesBusy => IsTranscribing || IsApplyingSubtitles;

    public bool IsTranscriptionProgressVisible => IsTranscribing && TranscriptionProgress > 0;

    public string SubtitlesLoadingTitle => IsApplyingSubtitles
        ? "Adding subtitles to timeline..."
        : "Generating subtitles...";

    public bool HasTranscriptionStatus => !string.IsNullOrWhiteSpace(TranscriptionStatus);

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

    public IBrush SelectedOutlineColorPreviewBrush => new SolidColorBrush(Color.FromRgb(
        NormalizeColorChannel(SelectedOutlineColorR),
        NormalizeColorChannel(SelectedOutlineColorG),
        NormalizeColorChannel(SelectedOutlineColorB)));

    public string SelectedOutlineColorHex => $"#{NormalizeColorChannel(SelectedOutlineColorR):X2}{NormalizeColorChannel(SelectedOutlineColorG):X2}{NormalizeColorChannel(SelectedOutlineColorB):X2}";
}
