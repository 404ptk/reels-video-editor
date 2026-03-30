using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReelsVideoEditor.App.Services.Text;
using ReelsVideoEditor.App.ViewModels.Timeline;

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
        AvailableFonts = LoadAvailableFonts(RenderableFonts);
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

    public bool HasPresetSaveStatus => !string.IsNullOrWhiteSpace(PresetSaveStatus);

    private bool CanSavePreset => HasSelectedTextClip && IsEditorVisible;

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
                SelectedClipFontFamily = ResolveAvailableFontFamily(state.FontFamily);
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

    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    private void SavePreset()
    {
        var preferredName = string.IsNullOrWhiteSpace(NewPresetName)
            ? BuildDefaultPresetName()
            : NewPresetName.Trim();

        var uniqueName = EnsureUniquePresetName(preferredName);
        var preset = new Models.TextPresetDefinition(
            uniqueName,
            ResolveAvailableFontFamily(SelectedClipFontFamily),
            Math.Clamp(SelectedClipFontSize, 10, 180),
            SelectedColorHex);

        UpsertPreset(preset, isBuiltIn: false);
        PersistCustomPresets();

        NewPresetName = string.Empty;
        PresetSaveStatus = $"Saved preset: {uniqueName}";
    }

    partial void OnIsEditorVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPresetVisible));
        SavePresetCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasSelectedTextClipChanged(bool value)
    {
        SavePresetCommand.NotifyCanExecuteChanged();
    }

    partial void OnPresetSaveStatusChanged(string value)
    {
        OnPropertyChanged(nameof(HasPresetSaveStatus));
    }

    partial void OnNewPresetNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(PresetSaveStatus))
        {
            PresetSaveStatus = string.Empty;
        }
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

    partial void OnSelectedClipFontFamilyChanged(string value)
    {
        ApplySelectedTextSettings();
    }

    private void ApplySelectedTextSettings()
    {
        if (isSyncingFromTimeline || !HasSelectedTextClip || !IsEditorVisible)
        {
            return;
        }

        ApplySelectedTextSettingsRequested?.Invoke(
            SelectedClipText,
            SelectedColorHex,
            SelectedClipFontSize,
            ResolveAvailableFontFamily(SelectedClipFontFamily));
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

    private string ResolveAvailableFontFamily(string? fontFamily)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            var requested = fontFamily.Trim();

            var exactRenderable = RenderableFonts.FirstOrDefault(font =>
                string.Equals(font, requested, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exactRenderable))
            {
                return exactRenderable;
            }

            var normalizedRequested = NormalizeFontLookup(requested);
            if (normalizedRequested.Length > 0)
            {
                var normalizedExact = RenderableFonts.FirstOrDefault(font =>
                    string.Equals(NormalizeFontLookup(font), normalizedRequested, StringComparison.Ordinal));
                if (!string.IsNullOrWhiteSpace(normalizedExact))
                {
                    return normalizedExact;
                }

                var aliasMatch = RenderableFonts
                    .Select(font => new { Font = font, Normalized = NormalizeFontLookup(font) })
                    .Where(item => item.Normalized.Length > 0
                        && (normalizedRequested.StartsWith(item.Normalized, StringComparison.Ordinal)
                            || item.Normalized.StartsWith(normalizedRequested, StringComparison.Ordinal)))
                    .OrderBy(item => Math.Abs(item.Normalized.Length - normalizedRequested.Length))
                    .ThenBy(item => item.Font, StringComparer.OrdinalIgnoreCase)
                    .Select(item => item.Font)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(aliasMatch))
                {
                    return aliasMatch;
                }
            }
        }

        return RenderableFonts.FirstOrDefault() ?? AvailableFonts.FirstOrDefault() ?? "Inter";
    }

    private static IReadOnlyList<string> LoadRenderableFonts()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in FontManager.Current.SystemFonts)
        {
            if (!string.IsNullOrWhiteSpace(family.Name))
            {
                names.Add(family.Name);
            }
        }

        if (names.Count == 0)
        {
            names.Add("Inter");
        }

        return names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> LoadAvailableFonts(IReadOnlyList<string> renderableFonts)
    {
        var names = new HashSet<string>(renderableFonts, StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            AddWindowsRegistryFonts(names, Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
            AddWindowsRegistryFonts(names, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
        }

        if (names.Count == 0)
        {
            names.Add("Inter");
        }

        return names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeFontLookup(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsRegistryFonts(HashSet<string> names, RegistryKey root, string subKeyPath)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath);
            if (key is null)
            {
                return;
            }

            foreach (var valueName in key.GetValueNames())
            {
                var normalizedName = NormalizeRegistryFontName(valueName);
                if (!string.IsNullOrWhiteSpace(normalizedName))
                {
                    names.Add(normalizedName);
                }
            }
        }
        catch
        {
            // Ignore registry access problems and continue with discovered fonts.
        }
    }

    private static string NormalizeRegistryFontName(string? valueName)
    {
        if (string.IsNullOrWhiteSpace(valueName))
        {
            return string.Empty;
        }

        var separatorIndex = valueName.IndexOf(" (", StringComparison.Ordinal);
        return separatorIndex > 0
            ? valueName[..separatorIndex].Trim()
            : valueName.Trim();
    }

    private void LoadPresets()
    {
        foreach (var defaultPreset in DefaultPresets)
        {
            UpsertPreset(defaultPreset, isBuiltIn: true);
        }

        var customPresets = presetStorage.LoadCustomPresets();
        foreach (var customPreset in customPresets)
        {
            var baseName = string.IsNullOrWhiteSpace(customPreset.Name)
                ? BuildDefaultPresetName()
                : customPreset.Name;

            var uniqueName = EnsureUniquePresetName(baseName);
            var normalizedPreset = new Models.TextPresetDefinition(
                uniqueName,
                ResolveAvailableFontFamily(customPreset.FontFamily),
                Math.Clamp(customPreset.FontSize, 10, 180),
                NormalizeHexColor(customPreset.ColorHex));

            UpsertPreset(normalizedPreset, isBuiltIn: false);
        }
    }

    private void PersistCustomPresets()
    {
        var customPresets = Presets
            .Where(preset => !builtInPresetNames.Contains(preset.Name))
            .ToArray();

        presetStorage.SaveCustomPresets(customPresets);
    }

    private void UpsertPreset(Models.TextPresetDefinition preset, bool isBuiltIn)
    {
        if (isBuiltIn)
        {
            builtInPresetNames.Add(preset.Name);
        }

        var existingIndex = FindPresetIndexByName(preset.Name);
        if (existingIndex >= 0)
        {
            Presets[existingIndex] = preset;
            return;
        }

        Presets.Add(preset);
    }

    private int FindPresetIndexByName(string name)
    {
        for (var i = 0; i < Presets.Count; i++)
        {
            if (string.Equals(Presets[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private string EnsureUniquePresetName(string baseName)
    {
        var normalizedBaseName = string.IsNullOrWhiteSpace(baseName)
            ? "My preset"
            : baseName.Trim();

        var candidate = normalizedBaseName;
        var suffix = 2;
        while (FindPresetIndexByName(candidate) >= 0)
        {
            candidate = $"{normalizedBaseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private string BuildDefaultPresetName()
    {
        return "My preset";
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
