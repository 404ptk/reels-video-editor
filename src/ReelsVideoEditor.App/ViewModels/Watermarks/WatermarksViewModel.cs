using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReelsVideoEditor.App.Models;
using ReelsVideoEditor.App.Services.Watermarks;

namespace ReelsVideoEditor.App.ViewModels.Watermarks;

public sealed partial class WatermarksViewModel : ViewModelBase
{
    private static readonly WatermarkPresetDefinition AddPresetTile = new("+", string.Empty, 0.6, IsAddTile: true);

    private readonly HashSet<string> builtInPresetNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly WatermarkPresetStorageService presetStorage = new();

    public string Title { get; } = "Watermarks";

    public string Description { get; } = "Create image watermark presets and control opacity.";

    public string EditorTitle { get; } = "Edit watermark preset";

    public string EditorDescription { get; } = "Load an image and tune watermark opacity.";

    public ObservableCollection<WatermarkPresetDefinition> Presets { get; } = [];

    public IEnumerable<WatermarkPresetDefinition> PresetTiles => Presets.Concat([AddPresetTile]);

    public bool IsPresetVisible => !IsEditorVisible;

    public bool HasSelectedImage => !string.IsNullOrWhiteSpace(SelectedImagePath) && File.Exists(SelectedImagePath);

    public Avalonia.Media.IImage? SelectedImagePreview => WatermarkPresetTilePreviewService.GetOrCreate(SelectedImagePath);

    public string SelectedOpacityPercent => $"{Math.Round(Math.Clamp(SelectedOpacity, 0.0, 1.0) * 100.0, MidpointRounding.AwayFromZero):0}%";

    public bool HasPresetSaveStatus => !string.IsNullOrWhiteSpace(PresetSaveStatus);

    public string SavePresetButtonLabel => IsEditingPreset ? "Update preset" : "Save preset";

    public string DeletePresetButtonLabel => IsDeletePendingForCurrentPreset ? "Confirm delete" : "Delete preset";

    public bool IsDeletePendingForCurrentPreset => IsEditingPreset && IsPendingDeletePreset(EditingPresetSourceName);

    private bool CanSavePreset => IsEditorVisible;

    private bool CanDeletePreset => IsEditingPreset && !string.IsNullOrWhiteSpace(EditingPresetSourceName);

    public WatermarksViewModel()
    {
        Presets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(PresetTiles));
        LoadPresets();
    }

    [ObservableProperty]
    private bool isEditorVisible;

    [ObservableProperty]
    private string selectedImagePath = string.Empty;

    [ObservableProperty]
    private double selectedOpacity = 0.6;

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

    [RelayCommand]
    private void CreatePresetFromTile()
    {
        ClearDeleteConfirmation();
        ExitPresetEditMode();
        NewPresetName = string.Empty;
        PresetSaveStatus = string.Empty;
        IsEditorVisible = true;
    }

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
        if (!HasSelectedImage)
        {
            PresetSaveStatus = "Load an image first.";
            return;
        }

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
        var preset = new WatermarkPresetDefinition(
            uniqueName,
            SelectedImagePath,
            Math.Clamp(SelectedOpacity, 0.0, 1.0));

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
    private void EditPresetFromTile(WatermarkPresetDefinition? preset)
    {
        if (preset is null)
        {
            return;
        }

        BeginPresetEdit(preset);
    }

    [RelayCommand(CanExecute = nameof(CanManagePreset))]
    private void DeletePresetFromTile(WatermarkPresetDefinition? preset)
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

    [RelayCommand]
    private void SelectImagePath(string? imagePath)
    {
        if (!IsSupportedImagePath(imagePath))
        {
            PresetSaveStatus = "Unsupported image file.";
            return;
        }

        SelectedImagePath = imagePath!;
        PresetSaveStatus = string.Empty;
    }

    public void BeginPresetEdit(WatermarkPresetDefinition preset)
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

        ClearDeleteConfirmation();
        IsEditingPreset = true;
        EditingPresetSourceName = preset.Name;
        NewPresetName = preset.Name;
        SelectedImagePath = preset.ImagePath;
        SelectedOpacity = Math.Clamp(preset.Opacity, 0.0, 1.0);
        IsEditorVisible = true;
        PresetSaveStatus = string.Empty;
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

        if (!HasSelectedImage)
        {
            PresetSaveStatus = "Load an image first.";
            return;
        }

        var preferredName = string.IsNullOrWhiteSpace(NewPresetName)
            ? EditingPresetSourceName
            : NewPresetName.Trim();

        var uniqueName = EnsureUniquePresetName(preferredName, EditingPresetSourceName);
        var updatedPreset = new WatermarkPresetDefinition(
            uniqueName,
            SelectedImagePath,
            Math.Clamp(SelectedOpacity, 0.0, 1.0));

        Presets[existingIndex] = updatedPreset;
        PersistCustomPresets();
        ClearDeleteConfirmation();

        ExitPresetEditMode();
        IsEditorVisible = false;
        PresetSaveStatus = $"Updated preset: {uniqueName}";
    }

    private void LoadPresets()
    {
        var customPresets = presetStorage.LoadCustomPresets();
        foreach (var customPreset in customPresets)
        {
            var baseName = string.IsNullOrWhiteSpace(customPreset.Name)
                ? BuildDefaultPresetName()
                : customPreset.Name;

            var uniqueName = EnsureUniquePresetName(baseName, excludedName: null);
            var normalizedPreset = new WatermarkPresetDefinition(
                uniqueName,
                customPreset.ImagePath,
                Math.Clamp(customPreset.Opacity, 0.0, 1.0));

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

    private void UpsertPreset(WatermarkPresetDefinition preset, bool isBuiltIn)
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

    private bool CanManagePreset(WatermarkPresetDefinition? preset)
    {
        return preset is not null && !preset.IsAddTile && !IsBuiltInPresetName(preset.Name);
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

    private string BuildDefaultPresetName()
    {
        return "Watermark preset";
    }

    private string EnsureUniquePresetName(string baseName, string? excludedName)
    {
        var normalizedBaseName = string.IsNullOrWhiteSpace(baseName)
            ? BuildDefaultPresetName()
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

    private void ExitPresetEditMode()
    {
        IsEditingPreset = false;
        EditingPresetSourceName = string.Empty;
        NewPresetName = string.Empty;
    }

    private static bool IsSupportedImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return false;
        }

        var extension = Path.GetExtension(imagePath);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnIsEditorVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPresetVisible));
        SavePresetCommand.NotifyCanExecuteChanged();
        DeletePresetCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsEditingPresetChanged(bool value)
    {
        OnPropertyChanged(nameof(SavePresetButtonLabel));
        OnPropertyChanged(nameof(DeletePresetButtonLabel));
        OnPropertyChanged(nameof(IsDeletePendingForCurrentPreset));
        SavePresetCommand.NotifyCanExecuteChanged();
        DeletePresetCommand.NotifyCanExecuteChanged();
    }

    partial void OnEditingPresetSourceNameChanged(string value)
    {
        OnPropertyChanged(nameof(DeletePresetButtonLabel));
        OnPropertyChanged(nameof(IsDeletePendingForCurrentPreset));
        DeletePresetCommand.NotifyCanExecuteChanged();
    }

    partial void OnPendingDeletePresetNameChanged(string value)
    {
        OnPropertyChanged(nameof(DeletePresetButtonLabel));
        OnPropertyChanged(nameof(IsDeletePendingForCurrentPreset));
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

    partial void OnSelectedImagePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasSelectedImage));
        OnPropertyChanged(nameof(SelectedImagePreview));
    }

    partial void OnSelectedOpacityChanged(double value)
    {
        var clamped = Math.Clamp(value, 0.0, 1.0);
        if (!clamped.Equals(value))
        {
            SelectedOpacity = clamped;
            return;
        }

        OnPropertyChanged(nameof(SelectedOpacityPercent));
    }
}
