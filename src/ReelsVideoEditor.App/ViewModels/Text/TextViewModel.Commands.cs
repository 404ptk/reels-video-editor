using System;
using CommunityToolkit.Mvvm.Input;

namespace ReelsVideoEditor.App.ViewModels.Text;

public sealed partial class TextViewModel
{
    [RelayCommand]
    private void BackToPresets()
    {
        ClearDeleteConfirmation();
        ExitPresetEditMode();
        IsEditorVisible = false;
    }

    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    private void SavePreset()
    {
        if (IsEditingPreset)
        {
            SavePresetEdits();
            return;
        }

        ClearDeleteConfirmation();

        var preferredName = string.IsNullOrWhiteSpace(NewPresetName)
            ? BuildDefaultPresetName()
            : NewPresetName.Trim();

        var uniqueName = EnsureUniquePresetName(preferredName, excludedName: null);
        var preset = new Models.TextPresetDefinition(
            uniqueName,
            ResolveAvailableFontFamily(SelectedClipFontFamily),
            Math.Clamp(SelectedClipFontSize, 10, 180),
            SelectedColorHex,
            SelectedOutlineColorHex,
            NormalizeOutlineThickness(SelectedClipOutlineThickness),
            IsAutoCaptions: autoCaptionsPresetMode);

        UpsertPreset(preset, isBuiltIn: false);
        PersistCustomPresets();

        NewPresetName = string.Empty;
        PresetSaveStatus = $"Saved preset: {uniqueName}";
    }

    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private void DeletePreset()
    {
        if (!IsEditingPreset || string.IsNullOrWhiteSpace(EditingPresetSourceName))
        {
            return;
        }

        if (!ConfirmDeleteRequest(EditingPresetSourceName))
        {
            return;
        }

        DeletePresetByName(EditingPresetSourceName, closeEditorIfDeleted: true);
    }

    [RelayCommand(CanExecute = nameof(CanManagePreset))]
    private void EditPresetFromTile(Models.TextPresetDefinition? preset)
    {
        if (preset is null)
        {
            return;
        }

        BeginPresetEdit(preset);
    }

    [RelayCommand(CanExecute = nameof(CanManagePreset))]
    private void DeletePresetFromTile(Models.TextPresetDefinition? preset)
    {
        if (preset is null)
        {
            return;
        }

        if (!ConfirmDeleteRequest(preset.Name))
        {
            return;
        }

        DeletePresetByName(preset.Name, closeEditorIfDeleted: false);
    }
}
