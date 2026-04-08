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
            var isCorrectMode = state.HasSelection && state.IsSubtitle == IsSubtitlesMode;
            HasSelectedTextClip = isCorrectMode;
            if (isCorrectMode)
            {
                SelectedClipText = state.Text;
                ApplyColorFromHex(state.ColorHex);
                ApplyOutlineColorFromHex(state.OutlineColorHex);
                SelectedClipOutlineThickness = NormalizeOutlineThickness(state.OutlineThickness);
                SelectedClipFontSize = state.FontSize;
                SelectedClipFontFamily = ResolveAvailableFontFamily(state.FontFamily);
                SelectedClipLineHeightMultiplier = NormalizeLineHeightMultiplier(state.LineHeightMultiplier);
                SelectedClipLetterSpacing = NormalizeLetterSpacing(state.LetterSpacing);
                SelectedClipTextRevealEffect = NormalizeTextRevealEffect(state.TextRevealEffect);
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

    partial void OnTranscriptionStatusChanged(string value)
    {
        OnPropertyChanged(nameof(HasTranscriptionStatus));
    }

    partial void OnIsTranscribingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSubtitlesBusy));
        OnPropertyChanged(nameof(IsTranscriptionProgressVisible));
        OnPropertyChanged(nameof(SubtitlesLoadingTitle));
    }

    partial void OnIsApplyingSubtitlesChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSubtitlesBusy));
        OnPropertyChanged(nameof(SubtitlesLoadingTitle));
    }

    partial void OnTranscriptionProgressChanged(double value)
    {
        OnPropertyChanged(nameof(IsTranscriptionProgressVisible));
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

    partial void OnSelectedOutlineColorRChanged(double value)
    {
        OnPropertyChanged(nameof(SelectedOutlineColorPreviewBrush));
        OnPropertyChanged(nameof(SelectedOutlineColorHex));
        ApplySelectedTextSettings();
    }

    partial void OnSelectedOutlineColorGChanged(double value)
    {
        OnPropertyChanged(nameof(SelectedOutlineColorPreviewBrush));
        OnPropertyChanged(nameof(SelectedOutlineColorHex));
        ApplySelectedTextSettings();
    }

    partial void OnSelectedOutlineColorBChanged(double value)
    {
        OnPropertyChanged(nameof(SelectedOutlineColorPreviewBrush));
        OnPropertyChanged(nameof(SelectedOutlineColorHex));
        ApplySelectedTextSettings();
    }

    partial void OnSelectedClipFontSizeChanged(double value)
    {
        ApplySelectedTextSettings();
    }

    partial void OnSelectedClipFontFamilyChanged(string value)
    {
        RefreshFilteredFonts();
        if (!string.IsNullOrWhiteSpace(value)
            && string.IsNullOrWhiteSpace(FontSearchQuery)
            && IsFontDropdownOpen)
        {
            IsFontDropdownOpen = false;
        }

        ApplySelectedTextSettings();
    }

    partial void OnFontSearchQueryChanged(string value)
    {
        RefreshFilteredFonts();
        IsFontDropdownOpen = IsEditorVisible;
    }

    partial void OnSelectedClipOutlineThicknessChanged(double value)
    {
        ApplySelectedTextSettings();
    }

    partial void OnSelectedClipLineHeightMultiplierChanged(double value)
    {
        ApplySelectedTextSettings();
    }

    partial void OnSelectedClipLetterSpacingChanged(double value)
    {
        ApplySelectedTextSettings();
    }

    partial void OnSelectedClipTextRevealEffectChanged(string value)
    {
        var normalized = NormalizeTextRevealEffect(value);
        if (!string.Equals(value, normalized, System.StringComparison.Ordinal))
        {
            SelectedClipTextRevealEffect = normalized;
            return;
        }

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
            ResolveAvailableFontFamily(SelectedClipFontFamily),
            SelectedOutlineColorHex,
            NormalizeOutlineThickness(SelectedClipOutlineThickness),
            NormalizeLineHeightMultiplier(SelectedClipLineHeightMultiplier),
            NormalizeLetterSpacing(SelectedClipLetterSpacing),
            NormalizeTextRevealEffect(SelectedClipTextRevealEffect));
    }
}
