using System;
using System.Linq;

namespace ReelsVideoEditor.App.ViewModels.Text;

public sealed partial class TextViewModel
{
    public void BeginPresetEdit(Models.TextPresetDefinition preset)
    {
        if (preset is null)
        {
            return;
        }

        if (IsBuiltInPresetName(preset.Name))
        {
            PresetSaveStatus = "Built-in presets cannot be edited.";
            return;
        }

        isSyncingFromTimeline = true;
        try
        {
            ClearDeleteConfirmation();
            IsEditingPreset = true;
            EditingPresetSourceName = preset.Name;
            NewPresetName = preset.Name;
            ApplyColorFromHex(preset.ColorHex);
            ApplyOutlineColorFromHex(preset.OutlineColorHex);
            SelectedClipOutlineThickness = NormalizeOutlineThickness(preset.OutlineThickness);
            SelectedClipFontSize = Math.Clamp(preset.FontSize, 10, 180);
            SelectedClipFontFamily = ResolveAvailableFontFamily(preset.FontFamily);
            SelectedClipLineHeightMultiplier = NormalizeLineHeightMultiplier(preset.LineHeightMultiplier);
            SelectedClipLetterSpacing = NormalizeLetterSpacing(preset.LetterSpacing);
            IsEditorVisible = true;
        }
        finally
        {
            isSyncingFromTimeline = false;
        }

        PresetSaveStatus = string.Empty;
    }

    private void LoadPresets()
    {
        foreach (var defaultPreset in defaultPresets)
        {
            UpsertPreset(defaultPreset, isBuiltIn: true);
        }

        var customPresets = presetStorage.LoadCustomPresets();
        foreach (var customPreset in customPresets)
        {
            var baseName = string.IsNullOrWhiteSpace(customPreset.Name)
                ? BuildDefaultPresetName()
                : customPreset.Name;

            var uniqueName = EnsureUniquePresetName(baseName, excludedName: null);
            var normalizedPreset = new Models.TextPresetDefinition(
                uniqueName,
                ResolveAvailableFontFamily(customPreset.FontFamily),
                Math.Clamp(customPreset.FontSize, 10, 180),
                NormalizeHexColor(customPreset.ColorHex),
                NormalizeOutlineHexColor(customPreset.OutlineColorHex),
                NormalizeOutlineThickness(customPreset.OutlineThickness),
                NormalizeLineHeightMultiplier(customPreset.LineHeightMultiplier),
                NormalizeLetterSpacing(customPreset.LetterSpacing),
                IsAutoCaptions: autoCaptionsPresetMode);

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

    private bool IsBuiltInPresetName(string name)
    {
        return builtInPresetNames.Contains(name);
    }

    private bool CanManagePreset(Models.TextPresetDefinition? preset)
    {
        return preset is not null && !IsBuiltInPresetName(preset.Name);
    }

    private void DeletePresetByName(string presetName, bool closeEditorIfDeleted)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return;
        }

        if (IsBuiltInPresetName(presetName))
        {
            ClearDeleteConfirmation();
            PresetSaveStatus = "Built-in presets cannot be deleted.";
            return;
        }

        var index = FindPresetIndexByName(presetName);
        if (index < 0)
        {
            ClearDeleteConfirmation();
            PresetSaveStatus = "Preset no longer exists.";
            if (closeEditorIfDeleted)
            {
                ExitPresetEditMode();
                IsEditorVisible = false;
            }

            return;
        }

        var deletedName = Presets[index].Name;
        Presets.RemoveAt(index);
        PersistCustomPresets();
        ClearDeleteConfirmation();

        if (closeEditorIfDeleted
            || (IsEditingPreset && string.Equals(EditingPresetSourceName, deletedName, StringComparison.OrdinalIgnoreCase)))
        {
            ExitPresetEditMode();
            IsEditorVisible = false;
        }

        PresetSaveStatus = $"Deleted preset: {deletedName}";
    }

    private bool ConfirmDeleteRequest(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return false;
        }

        if (IsPendingDeletePreset(presetName))
        {
            return true;
        }

        PendingDeletePresetName = presetName;
        PresetSaveStatus = $"Click delete again to confirm: {presetName}";
        return false;
    }

    private bool IsPendingDeletePreset(string presetName)
    {
        return !string.IsNullOrWhiteSpace(presetName)
            && string.Equals(PendingDeletePresetName, presetName, StringComparison.OrdinalIgnoreCase);
    }

    private void ClearDeleteConfirmation()
    {
        PendingDeletePresetName = string.Empty;
    }

    private void SavePresetEdits()
    {
        if (!IsEditingPreset || string.IsNullOrWhiteSpace(EditingPresetSourceName))
        {
            return;
        }

        if (IsBuiltInPresetName(EditingPresetSourceName))
        {
            PresetSaveStatus = "Built-in presets cannot be edited.";
            return;
        }

        var existingIndex = FindPresetIndexByName(EditingPresetSourceName);
        if (existingIndex < 0)
        {
            PresetSaveStatus = "Preset no longer exists.";
            ExitPresetEditMode();
            return;
        }

        var preferredName = string.IsNullOrWhiteSpace(NewPresetName)
            ? EditingPresetSourceName
            : NewPresetName.Trim();

        var uniqueName = EnsureUniquePresetName(preferredName, EditingPresetSourceName);
        var updatedPreset = new Models.TextPresetDefinition(
            uniqueName,
            ResolveAvailableFontFamily(SelectedClipFontFamily),
            Math.Clamp(SelectedClipFontSize, 10, 180),
            SelectedColorHex,
            SelectedOutlineColorHex,
            NormalizeOutlineThickness(SelectedClipOutlineThickness),
            NormalizeLineHeightMultiplier(SelectedClipLineHeightMultiplier),
            NormalizeLetterSpacing(SelectedClipLetterSpacing),
            IsAutoCaptions: autoCaptionsPresetMode);

        Presets[existingIndex] = updatedPreset;
        PersistCustomPresets();
        ClearDeleteConfirmation();

        ExitPresetEditMode();
        IsEditorVisible = false;
        PresetSaveStatus = $"Updated preset: {uniqueName}";
    }

    private string EnsureUniquePresetName(string baseName, string? excludedName)
    {
        var normalizedBaseName = string.IsNullOrWhiteSpace(baseName)
            ? "My preset"
            : baseName.Trim();

        var candidate = normalizedBaseName;
        var suffix = 2;
        while (true)
        {
            var existingIndex = FindPresetIndexByName(candidate);
            if (existingIndex < 0)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(excludedName)
                && string.Equals(Presets[existingIndex].Name, excludedName, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            candidate = $"{normalizedBaseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private string BuildDefaultPresetName()
    {
        return autoCaptionsPresetMode ? "Auto captions preset" : "My preset";
    }

    private void ExitPresetEditMode()
    {
        IsEditingPreset = false;
        EditingPresetSourceName = string.Empty;
        NewPresetName = string.Empty;
    }
}
