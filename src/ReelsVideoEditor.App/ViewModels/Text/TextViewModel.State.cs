using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.ViewModels.Text;

public sealed partial class TextViewModel
{
    public void SyncSelectedTextClip(TimelineSelectedTextClipState state)
    {
        if (IsEditingPreset)
        {
            return;
        }

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

    partial void OnIsEditorVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPresetVisible));
        SavePresetCommand.NotifyCanExecuteChanged();
        DeletePresetCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasSelectedTextClipChanged(bool value)
    {
        SavePresetCommand.NotifyCanExecuteChanged();
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
        if (isSyncingFromTimeline || IsEditingPreset || !HasSelectedTextClip || !IsEditorVisible)
        {
            return;
        }

        ApplySelectedTextSettingsRequested?.Invoke(
            SelectedClipText,
            SelectedColorHex,
            SelectedClipFontSize,
            ResolveAvailableFontFamily(SelectedClipFontFamily));
    }
}
