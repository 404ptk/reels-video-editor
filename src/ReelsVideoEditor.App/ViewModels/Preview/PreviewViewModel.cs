using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.ViewModels.Preview;

public enum PreviewQuality
{
    High,
    Mid,
    Low
}

public sealed partial class PreviewViewModel : ViewModelBase
{
    private const string ZeroTime = "00:00:00";
    private readonly Stack<TransformCropState> transformCropUndoStack = new();
    private TransformCropState? pendingEditStartState;

    public Func<string?>? ResolveVideoPath { get; set; }

    public Func<long, IReadOnlyList<PreviewVideoLayer>>? ResolveVideoLayers { get; set; }

    public Func<long, PreviewAudioState>? ResolveAudioState { get; set; }

    public Func<bool>? HasSelectedVideoClip { get; set; }

    public Func<long>? ResolvePlaybackMaxMilliseconds { get; set; }

    public Action<long>? PlaybackTimeChanged { get; set; }

    public Action<bool>? PlaybackStateChanged { get; set; }

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private string? sourceVideoPath;

    [ObservableProperty]
    private WriteableBitmap? currentFrame;

    [ObservableProperty]
    private string fpsText = "0 FPS";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FpsDotColor))]
    private int fps;

    public string FpsDotColor => Fps switch
    {
        >= 50 => "#00FF00",
        >= 25 => "#FFFF00",
        _ => "#FF0000"
    };

    [ObservableProperty]
    private string zoomText = "Zoom: 100%";

    [ObservableProperty]
    private PreviewQuality selectedQuality = PreviewQuality.Mid;

    [ObservableProperty]
    private bool isTransformModeEnabled;

    [ObservableProperty]
    private bool isClipperModeEnabled;

    [ObservableProperty]
    private bool isTransformTargetActive = true;

    public bool ShowTransformHandles => IsTransformModeEnabled && IsVideoVisible && IsTransformTargetActive;
    public bool ShowClipperHandles => IsClipperModeEnabled && IsVideoVisible && IsTransformTargetActive;

    partial void OnIsTransformModeEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowTransformHandles));
        if (value)
            IsClipperModeEnabled = false;
    }

    partial void OnIsClipperModeEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowClipperHandles));
        if (value)
            IsTransformModeEnabled = false;
    }

    public PreviewQuality[] AvailableQualities { get; } = new[] 
    { 
        PreviewQuality.High, 
        PreviewQuality.Mid, 
        PreviewQuality.Low 
    };

    [ObservableProperty]
    private bool isAudioMuted;

    [ObservableProperty]
    private bool isVideoHidden;

    [ObservableProperty]
    private bool useBlurredBackground = true;

    [ObservableProperty]
    private double currentAudioVolume = 1.0;

    [ObservableProperty]
    private int stopRequestVersion;

    [ObservableProperty]
    private long currentPlaybackMilliseconds;

    [ObservableProperty]
    private string currentPlaybackTimeText = ZeroTime;

    [ObservableProperty]
    private string totalPlaybackTimeText = ZeroTime;

    [ObservableProperty]
    private long requestedSeekMilliseconds;

    [ObservableProperty]
    private int seekRequestVersion;

    [ObservableProperty]
    private bool isTextOverlayVisible;

    [ObservableProperty]
    private string textOverlayValue = string.Empty;

    [ObservableProperty]
    private string textOverlayFontFamily = "Inter";

    [ObservableProperty]
    private double textOverlayFontSize = 56;

    [ObservableProperty]
    private string textOverlayColor = "#FFFFFF";

    public string Title { get; } = "Preview";

    public string PlaceholderTitle { get; } = "No video loaded";

    public string PlaceholderSubtitle { get; } = "Drop a video here";

    public string PlayPauseIconPath => IsPlaying
        ? "M4,3 H7 V13 H4 Z M9,3 H12 V13 H9 Z"
        : "M4,3 L13,8 L4,13 Z";

    public bool HasVideoLoaded => !string.IsNullOrWhiteSpace(SourceVideoPath) && File.Exists(SourceVideoPath);

    public bool ShowPlaceholder => !HasVideoLoaded;

    public bool IsVideoVisible => HasVideoLoaded && !IsVideoHidden;

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (!IsPlaying)
        {
            if (!HasVideoLoaded)
            {
                var resolvedPath = ResolveVideoPath?.Invoke();
                if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
                {
                    return;
                }

                SourceVideoPath = resolvedPath;
            }

            IsPlaying = true;
            return;
        }

        IsPlaying = false;
    }

    [RelayCommand]
    public void Stop()
    {
        IsPlaying = false;
        StopRequestVersion++;
        CurrentPlaybackMilliseconds = 0;
        CurrentPlaybackTimeText = ZeroTime;
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayPauseIconPath));
        PlaybackStateChanged?.Invoke(value);
    }

    partial void OnSourceVideoPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasVideoLoaded));
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(IsVideoVisible));
        OnPropertyChanged(nameof(ShowTransformHandles));
        OnPropertyChanged(nameof(ShowClipperHandles));
        CurrentPlaybackMilliseconds = 0;
        CurrentPlaybackTimeText = ZeroTime;
        TotalPlaybackTimeText = ZeroTime;
    }

    partial void OnIsVideoHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVideoVisible));
        OnPropertyChanged(nameof(ShowTransformHandles));
        OnPropertyChanged(nameof(ShowClipperHandles));
    }

    partial void OnIsTransformTargetActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowTransformHandles));
        OnPropertyChanged(nameof(ShowClipperHandles));
    }

    public void UpdatePlaybackTime(long playbackMilliseconds)
    {
        var safeMilliseconds = Math.Max(0, playbackMilliseconds);
        CurrentPlaybackMilliseconds = safeMilliseconds;
        CurrentPlaybackTimeText = FormatPlaybackTime(safeMilliseconds);
    }

    public void UpdateTotalPlaybackTime(long totalPlaybackMilliseconds)
    {
        if (totalPlaybackMilliseconds <= 0)
        {
            return;
        }

        TotalPlaybackTimeText = FormatPlaybackTime(totalPlaybackMilliseconds);
    }

    public void SeekToPlaybackPosition(long targetPlaybackMilliseconds)
    {
        if (!HasVideoLoaded)
        {
            var resolvedPath = ResolveVideoPath?.Invoke();
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                return;
            }

            SourceVideoPath = resolvedPath;
        }

        var safeMilliseconds = Math.Max(0, targetPlaybackMilliseconds);
        RequestedSeekMilliseconds = safeMilliseconds;
        SeekRequestVersion++;
        UpdatePlaybackTime(safeMilliseconds);
    }

    private static string FormatPlaybackTime(long playbackMilliseconds)
    {
        var safeMilliseconds = Math.Max(0, playbackMilliseconds);
        var totalCentiseconds = safeMilliseconds / 10;
        var minutes = totalCentiseconds / 6000;
        var seconds = (totalCentiseconds / 100) % 60;
        var centiseconds = totalCentiseconds % 100;

        return $"{minutes:D2}:{seconds:D2}:{centiseconds:D2}";
    }

    partial void OnCurrentPlaybackMillisecondsChanged(long value)
    {
        PlaybackTimeChanged?.Invoke(value);
    }

    public void TriggerFrameUpdate()
    {
        OnPropertyChanged(nameof(CurrentFrame));
    }

    public void UpdateTextOverlayState(string? text, string? fontFamily, double fontSize, string? colorHex, bool isVisible)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            TextOverlayValue = text;
        }

        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            TextOverlayFontFamily = fontFamily;
        }

        if (fontSize > 0)
        {
            TextOverlayFontSize = fontSize;
        }

        if (!string.IsNullOrWhiteSpace(colorHex))
        {
            TextOverlayColor = colorHex;
        }

        IsTextOverlayVisible = isVisible;
    }

    [ObservableProperty]
    private double foregroundWidth;

    [ObservableProperty]
    private double foregroundHeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScaledForegroundWidth))]
    [NotifyPropertyChangedFor(nameof(ScaledForegroundHeight))]
    private double transformScale = 1.0;

    public double ScaledForegroundWidth => ForegroundWidth * TransformScale;
    public double ScaledForegroundHeight => ForegroundHeight * TransformScale;

    [ObservableProperty]
    private double transformX;

    [ObservableProperty]
    private double transformY;

    [ObservableProperty]
    private double previewFrameWidth = 1.0;

    [ObservableProperty]
    private double previewFrameHeight = 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransformHandleThickness))]
    [NotifyPropertyChangedFor(nameof(TransformHandleSize))]
    [NotifyPropertyChangedFor(nameof(TransformHandleOffset))]
    [NotifyPropertyChangedFor(nameof(HandleMarginTopLeft))]
    [NotifyPropertyChangedFor(nameof(HandleMarginTopRight))]
    [NotifyPropertyChangedFor(nameof(HandleMarginBottomLeft))]
    [NotifyPropertyChangedFor(nameof(HandleMarginBottomRight))]
    [NotifyPropertyChangedFor(nameof(HandleMarginTopCenter))]
    [NotifyPropertyChangedFor(nameof(HandleMarginBottomCenter))]
    [NotifyPropertyChangedFor(nameof(HandleMarginLeftCenter))]
    [NotifyPropertyChangedFor(nameof(HandleMarginRightCenter))]
    private double currentZoom = 1.0;

    public double TransformHandleThickness => 2.0 / CurrentZoom;
    public double TransformHandleSize => 8.0 / CurrentZoom;
    public double TransformHandleOffset => -4.0 / CurrentZoom;

    public Avalonia.Thickness HandleMarginTopLeft => new(TransformHandleOffset, TransformHandleOffset, 0, 0);
    public Avalonia.Thickness HandleMarginTopRight => new(0, TransformHandleOffset, TransformHandleOffset, 0);
    public Avalonia.Thickness HandleMarginBottomLeft => new(TransformHandleOffset, 0, 0, TransformHandleOffset);
    public Avalonia.Thickness HandleMarginBottomRight => new(0, 0, TransformHandleOffset, TransformHandleOffset);
    
    public Avalonia.Thickness HandleMarginTopCenter => new(0, TransformHandleOffset, 0, 0);
    public Avalonia.Thickness HandleMarginBottomCenter => new(0, 0, 0, TransformHandleOffset);
    public Avalonia.Thickness HandleMarginLeftCenter => new(TransformHandleOffset, 0, 0, 0);
    public Avalonia.Thickness HandleMarginRightCenter => new(0, 0, TransformHandleOffset, 0);

    [ObservableProperty]
    private double cropLeft;

    [ObservableProperty]
    private double cropTop;

    [ObservableProperty]
    private double cropRight;

    [ObservableProperty]
    private double cropBottom;

    public double CropOverlayLeft => ScaledForegroundWidth * CropLeft;
    public double CropOverlayTop => ScaledForegroundHeight * CropTop;
    public double CropOverlayWidth => Math.Max(0, ScaledForegroundWidth * (1.0 - CropLeft - CropRight));
    public double CropOverlayHeight => Math.Max(0, ScaledForegroundHeight * (1.0 - CropTop - CropBottom));
    public double CropHandleOffset => TransformHandleOffset;

    public double CropHandleTopLeftX => CropOverlayLeft + CropHandleOffset;
    public double CropHandleTopLeftY => CropOverlayTop + CropHandleOffset;
    public double CropHandleTopCenterX => CropOverlayLeft + (CropOverlayWidth / 2.0) + CropHandleOffset;
    public double CropHandleTopCenterY => CropOverlayTop + CropHandleOffset;
    public double CropHandleTopRightX => CropOverlayLeft + CropOverlayWidth + CropHandleOffset;
    public double CropHandleTopRightY => CropOverlayTop + CropHandleOffset;
    public double CropHandleLeftCenterX => CropOverlayLeft + CropHandleOffset;
    public double CropHandleLeftCenterY => CropOverlayTop + (CropOverlayHeight / 2.0) + CropHandleOffset;
    public double CropHandleRightCenterX => CropOverlayLeft + CropOverlayWidth + CropHandleOffset;
    public double CropHandleRightCenterY => CropOverlayTop + (CropOverlayHeight / 2.0) + CropHandleOffset;
    public double CropHandleBottomLeftX => CropOverlayLeft + CropHandleOffset;
    public double CropHandleBottomLeftY => CropOverlayTop + CropOverlayHeight + CropHandleOffset;
    public double CropHandleBottomCenterX => CropOverlayLeft + (CropOverlayWidth / 2.0) + CropHandleOffset;
    public double CropHandleBottomCenterY => CropOverlayTop + CropOverlayHeight + CropHandleOffset;
    public double CropHandleBottomRightX => CropOverlayLeft + CropOverlayWidth + CropHandleOffset;
    public double CropHandleBottomRightY => CropOverlayTop + CropOverlayHeight + CropHandleOffset;

    partial void OnForegroundWidthChanged(double value)
    {
        OnPropertyChanged(nameof(ScaledForegroundWidth));
        NotifyCropOverlayChanged();
    }

    partial void OnForegroundHeightChanged(double value)
    {
        OnPropertyChanged(nameof(ScaledForegroundHeight));
        NotifyCropOverlayChanged();
    }

    partial void OnTransformScaleChanged(double value) => NotifyCropOverlayChanged();
    partial void OnCurrentZoomChanged(double value) => NotifyCropOverlayChanged();
    partial void OnCropLeftChanged(double value) => NotifyCropOverlayChanged();
    partial void OnCropTopChanged(double value) => NotifyCropOverlayChanged();
    partial void OnCropRightChanged(double value) => NotifyCropOverlayChanged();
    partial void OnCropBottomChanged(double value) => NotifyCropOverlayChanged();

    private void NotifyCropOverlayChanged()
    {
        OnPropertyChanged(nameof(CropOverlayLeft));
        OnPropertyChanged(nameof(CropOverlayTop));
        OnPropertyChanged(nameof(CropOverlayWidth));
        OnPropertyChanged(nameof(CropOverlayHeight));
        OnPropertyChanged(nameof(CropHandleOffset));
        OnPropertyChanged(nameof(CropHandleTopLeftX));
        OnPropertyChanged(nameof(CropHandleTopLeftY));
        OnPropertyChanged(nameof(CropHandleTopCenterX));
        OnPropertyChanged(nameof(CropHandleTopCenterY));
        OnPropertyChanged(nameof(CropHandleTopRightX));
        OnPropertyChanged(nameof(CropHandleTopRightY));
        OnPropertyChanged(nameof(CropHandleLeftCenterX));
        OnPropertyChanged(nameof(CropHandleLeftCenterY));
        OnPropertyChanged(nameof(CropHandleRightCenterX));
        OnPropertyChanged(nameof(CropHandleRightCenterY));
        OnPropertyChanged(nameof(CropHandleBottomLeftX));
        OnPropertyChanged(nameof(CropHandleBottomLeftY));
        OnPropertyChanged(nameof(CropHandleBottomCenterX));
        OnPropertyChanged(nameof(CropHandleBottomCenterY));
        OnPropertyChanged(nameof(CropHandleBottomRightX));
        OnPropertyChanged(nameof(CropHandleBottomRightY));
    }

    public void BeginTransformCropEdit()
    {
        pendingEditStartState ??= CaptureTransformCropState();
    }

    public void CommitTransformCropEdit()
    {
        if (pendingEditStartState is null)
        {
            return;
        }

        var finalState = CaptureTransformCropState();
        if (!pendingEditStartState.Value.Equals(finalState))
        {
            transformCropUndoStack.Push(pendingEditStartState.Value);
        }

        pendingEditStartState = null;
    }

    public bool CanUndoTransformCrop => transformCropUndoStack.Count > 0;

    public void UndoTransformCrop()
    {
        if (transformCropUndoStack.Count == 0)
        {
            return;
        }

        pendingEditStartState = null;
        var previous = transformCropUndoStack.Pop();
        ApplyTransformCropState(previous);
    }

    private TransformCropState CaptureTransformCropState()
    {
        return new TransformCropState(
            TransformX,
            TransformY,
            TransformScale,
            CropLeft,
            CropTop,
            CropRight,
            CropBottom);
    }

    private void ApplyTransformCropState(TransformCropState state)
    {
        TransformX = state.TransformX;
        TransformY = state.TransformY;
        TransformScale = state.TransformScale;
        CropLeft = state.CropLeft;
        CropTop = state.CropTop;
        CropRight = state.CropRight;
        CropBottom = state.CropBottom;
    }

    private readonly record struct TransformCropState(
        double TransformX,
        double TransformY,
        double TransformScale,
        double CropLeft,
        double CropTop,
        double CropRight,
        double CropBottom);
}
