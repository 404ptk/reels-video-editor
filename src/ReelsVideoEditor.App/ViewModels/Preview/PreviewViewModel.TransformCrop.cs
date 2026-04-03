using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace ReelsVideoEditor.App.ViewModels.Preview;

public sealed partial class PreviewViewModel
{
    private readonly Stack<TransformCropState> transformCropUndoStack = new();
    private TransformCropState? pendingEditStartState;

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

    partial void OnPreviewFrameWidthChanged(double value)
    {
        UpdateTextOverlayLayouts();
    }

    partial void OnPreviewFrameHeightChanged(double value)
    {
        UpdateTextOverlayLayouts();
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
