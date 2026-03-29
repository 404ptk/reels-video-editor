using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ReelsVideoEditor.App.ViewModels.Preview;

namespace ReelsVideoEditor.App.Views.Preview;

public partial class PreviewPanelView
{
    private void OnPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (previewFrame is null || previewViewport is null || boundViewModel is null)
        {
            return;
        }

        var zoomDelta = e.Delta.Y > 0 ? 0.15 : -0.15;
        var newZoom = Math.Clamp(currentZoom + zoomDelta, 1.0, 5.0);

        if (Math.Abs(newZoom - currentZoom) < 0.001)
        {
            return;
        }

        var mousePos = e.GetPosition(previewViewport);
        var centerX = previewViewport.Bounds.Width / 2;
        var centerY = previewViewport.Bounds.Height / 2;

        var relativeX = mousePos.X - centerX;
        var relativeY = mousePos.Y - centerY;

        var zoomRatio = newZoom / currentZoom;
        panX = relativeX - (relativeX - panX) * zoomRatio;
        panY = relativeY - (relativeY - panY) * zoomRatio;

        currentZoom = newZoom;

        boundViewModel.CurrentZoom = newZoom;
        boundViewModel.ZoomText = $"Zoom: {Math.Round(currentZoom * 100)}%";

        ConstrainPan();
        ApplyTransform();
        e.Handled = true;
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (previewViewport is null || boundViewModel is null)
        {
            return;
        }

        previewViewport.Focus();

        var pointer = e.GetCurrentPoint(previewViewport);

        if (boundViewModel.IsTransformModeEnabled && e.Source is Avalonia.Controls.Shapes.Ellipse ellipse && ellipse.Classes.Contains("TransformHandle"))
        {
            if (pointer.Properties.IsLeftButtonPressed)
            {
                BeginTransformCropEditSession();
                isScaling = true;
                scaleStartValue = boundViewModel.TransformScale;
                var vpCenter = new Point(previewViewport.Bounds.Width / 2, previewViewport.Bounds.Height / 2);
                dragCenter = new Point(vpCenter.X + boundViewModel.TransformX, vpCenter.Y + boundViewModel.TransformY);
                scaleStartDistance = Math.Sqrt(Math.Pow(pointer.Position.X - dragCenter.X, 2) + Math.Pow(pointer.Position.Y - dragCenter.Y, 2));
                e.Handled = true;
                return;
            }
        }

        if (boundViewModel.IsClipperModeEnabled && e.Source is Avalonia.Controls.Shapes.Ellipse cropEllipse && cropEllipse.Classes.Contains("ClipperHandle"))
        {
            if (pointer.Properties.IsLeftButtonPressed)
            {
                activeCropHandle = ParseCropHandle(cropEllipse.Tag as string);
                if (activeCropHandle != CropHandle.None)
                {
                    BeginTransformCropEditSession();
                    isCropping = true;
                    e.Handled = true;
                    return;
                }
            }
        }

        var canPanZoom = currentZoom > 1.0 && !boundViewModel.IsTransformModeEnabled && !boundViewModel.IsClipperModeEnabled;
        var canPanTransform = boundViewModel.IsTransformModeEnabled;

        if (!canPanZoom && !canPanTransform)
        {
            return;
        }

        if (pointer.Properties.IsLeftButtonPressed || pointer.Properties.IsMiddleButtonPressed)
        {
            if (canPanTransform)
            {
                BeginTransformCropEditSession();
            }

            isPanning = true;
            lastPanPosition = pointer.Position;
            e.Handled = true;
        }
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (previewViewport is null || boundViewModel is null)
        {
            return;
        }

        var pointer = e.GetCurrentPoint(previewViewport);

        if (isScaling)
        {
            var currentDistance = Math.Sqrt(Math.Pow(pointer.Position.X - dragCenter.X, 2) + Math.Pow(pointer.Position.Y - dragCenter.Y, 2));
            if (scaleStartDistance > 0)
            {
                var newScale = scaleStartValue * (currentDistance / scaleStartDistance);
                boundViewModel.TransformScale = Math.Max(0.1, newScale);
            }

            e.Handled = true;
            return;
        }

        if (isCropping)
        {
            ApplyCropDrag(pointer.Position);
            e.Handled = true;
            return;
        }

        if (!isPanning)
        {
            return;
        }

        var deltaX = pointer.Position.X - lastPanPosition.X;
        var deltaY = pointer.Position.Y - lastPanPosition.Y;

        lastPanPosition = pointer.Position;

        if (boundViewModel.IsTransformModeEnabled)
        {
            boundViewModel.TransformX += deltaX;
            boundViewModel.TransformY += deltaY;
            e.Handled = true;
            return;
        }

        panX += deltaX;
        panY += deltaY;

        ConstrainPan();
        ApplyTransform();
        e.Handled = true;
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndTransformCropEditSession();
        isPanning = false;
        isScaling = false;
        isCropping = false;
        activeCropHandle = CropHandle.None;
    }

    private void OnPreviewPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndTransformCropEditSession();
        isPanning = false;
        isScaling = false;
        isCropping = false;
        activeCropHandle = CropHandle.None;
    }

    private void OnPreviewViewportKeyDown(object? sender, KeyEventArgs e)
    {
        if (boundViewModel is null)
        {
            return;
        }

        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            boundViewModel.UndoTransformCrop();
            e.Handled = true;
        }
    }

    private void BeginTransformCropEditSession()
    {
        if (hasActiveTransformCropEdit || boundViewModel is null)
        {
            return;
        }

        hasActiveTransformCropEdit = true;
        boundViewModel.BeginTransformCropEdit();
    }

    private void EndTransformCropEditSession()
    {
        if (!hasActiveTransformCropEdit || boundViewModel is null)
        {
            return;
        }

        hasActiveTransformCropEdit = false;
        boundViewModel.CommitTransformCropEdit();
    }

    private void ApplyCropDrag(Point pointerPosition)
    {
        if (boundViewModel is null || activeCropHandle == CropHandle.None)
        {
            return;
        }

        var fgWidth = boundViewModel.ScaledForegroundWidth;
        var fgHeight = boundViewModel.ScaledForegroundHeight;
        if (fgWidth <= 1 || fgHeight <= 1)
        {
            return;
        }

        var centerX = (previewViewport?.Bounds.Width ?? 0) / 2.0;
        var centerY = (previewViewport?.Bounds.Height ?? 0) / 2.0;
        var fgLeft = centerX + boundViewModel.TransformX - (fgWidth / 2.0);
        var fgTop = centerY + boundViewModel.TransformY - (fgHeight / 2.0);

        var xNorm = (pointerPosition.X - fgLeft) / fgWidth;
        var yNorm = (pointerPosition.Y - fgTop) / fgHeight;
        xNorm = Math.Clamp(xNorm, 0.0, 1.0);
        yNorm = Math.Clamp(yNorm, 0.0, 1.0);

        var maxLeft = Math.Max(0.0, 1.0 - boundViewModel.CropRight - MinCropVisibleNormalized);
        var maxTop = Math.Max(0.0, 1.0 - boundViewModel.CropBottom - MinCropVisibleNormalized);
        var maxRight = Math.Max(0.0, 1.0 - boundViewModel.CropLeft - MinCropVisibleNormalized);
        var maxBottom = Math.Max(0.0, 1.0 - boundViewModel.CropTop - MinCropVisibleNormalized);

        if (activeCropHandle is CropHandle.TopLeft or CropHandle.Left or CropHandle.BottomLeft)
        {
            boundViewModel.CropLeft = Math.Clamp(xNorm, 0.0, maxLeft);
        }

        if (activeCropHandle is CropHandle.TopRight or CropHandle.Right or CropHandle.BottomRight)
        {
            var right = 1.0 - xNorm;
            boundViewModel.CropRight = Math.Clamp(right, 0.0, maxRight);
        }

        if (activeCropHandle is CropHandle.TopLeft or CropHandle.Top or CropHandle.TopRight)
        {
            boundViewModel.CropTop = Math.Clamp(yNorm, 0.0, maxTop);
        }

        if (activeCropHandle is CropHandle.BottomLeft or CropHandle.Bottom or CropHandle.BottomRight)
        {
            var bottom = 1.0 - yNorm;
            boundViewModel.CropBottom = Math.Clamp(bottom, 0.0, maxBottom);
        }
    }

    private static CropHandle ParseCropHandle(string? tag) =>
        tag switch
        {
            "TopLeft" => CropHandle.TopLeft,
            "Top" => CropHandle.Top,
            "TopRight" => CropHandle.TopRight,
            "Left" => CropHandle.Left,
            "Right" => CropHandle.Right,
            "BottomLeft" => CropHandle.BottomLeft,
            "Bottom" => CropHandle.Bottom,
            "BottomRight" => CropHandle.BottomRight,
            _ => CropHandle.None
        };

    private void ConstrainPan()
    {
        if (previewFrame is null)
        {
            return;
        }

        if (boundViewModel?.IsTransformModeEnabled == true)
        {
            return;
        }

        if (currentZoom <= 1.0)
        {
            panX = 0;
            panY = 0;
            return;
        }

        var scaledWidth = previewFrame.Width * currentZoom;
        var scaledHeight = previewFrame.Height * currentZoom;

        var maxPanX = Math.Max(0, (scaledWidth - previewFrame.Width) / 2);
        var maxPanY = Math.Max(0, (scaledHeight - previewFrame.Height) / 2);

        panX = Math.Clamp(panX, -maxPanX, maxPanX);
        panY = Math.Clamp(panY, -maxPanY, maxPanY);
    }

    private void ApplyTransform()
    {
        if (previewFrame?.RenderTransform is TransformGroup group && group.Children.Count >= 2)
        {
            if (group.Children[0] is ScaleTransform scale)
            {
                scale.ScaleX = currentZoom;
                scale.ScaleY = currentZoom;
            }

            if (group.Children[1] is TranslateTransform translate)
            {
                translate.X = panX;
                translate.Y = panY;
            }
        }
    }

    private void UpdatePreviewFrameSize()
    {
        if (previewFrame is null || previewViewport is null)
        {
            return;
        }

        var availableWidth = Math.Max(0, previewViewport.Bounds.Width - PreviewPadding * 2);
        var availableHeight = Math.Max(0, previewViewport.Bounds.Height - PreviewPadding * 2);

        if (availableWidth <= 0 || availableHeight <= 0)
        {
            return;
        }

        var frameWidthFromHeight = availableHeight * PreviewAspectRatio;
        var frameWidth = Math.Min(availableWidth, frameWidthFromHeight);
        var frameHeight = frameWidth / PreviewAspectRatio;

        var previousFrameWidth = currentPreviewFrameWidth;
        var previousFrameHeight = currentPreviewFrameHeight;

        previewFrame.Width = Math.Max(64, frameWidth);
        previewFrame.Height = Math.Max(112, frameHeight);
        currentPreviewFrameWidth = previewFrame.Width;
        currentPreviewFrameHeight = previewFrame.Height;

        if (boundViewModel is not null && hasInitializedPreviewFrameSize)
        {
            var safePrevWidth = Math.Max(1.0, previousFrameWidth);
            var safePrevHeight = Math.Max(1.0, previousFrameHeight);
            var ratioX = currentPreviewFrameWidth / safePrevWidth;
            var ratioY = currentPreviewFrameHeight / safePrevHeight;

            if (Math.Abs(ratioX - 1.0) > 0.0001 || Math.Abs(ratioY - 1.0) > 0.0001)
            {
                boundViewModel.TransformX *= ratioX;
                boundViewModel.TransformY *= ratioY;
            }
        }

        hasInitializedPreviewFrameSize = true;

        if (boundViewModel is not null)
        {
            boundViewModel.PreviewFrameWidth = currentPreviewFrameWidth;
            boundViewModel.PreviewFrameHeight = currentPreviewFrameHeight;
        }

        previewFrame.HorizontalAlignment = HorizontalAlignment.Center;
        previewFrame.VerticalAlignment = VerticalAlignment.Center;

        UpdateVideoForegroundBounds();

        ConstrainPan();
        ApplyTransform();
    }

    private void UpdateVideoForegroundBounds()
    {
        if (boundViewModel is null || previewFrame is null)
        {
            return;
        }

        if (UpdateTextForegroundBoundsIfNeeded(boundViewModel, boundViewModel.CurrentPlaybackMilliseconds))
        {
            return;
        }

        if (!decoder.IsOpen)
        {
            return;
        }

        var sourceW = decoder.FrameWidth;
        var sourceH = decoder.FrameHeight;
        if (sourceW <= 0 || sourceH <= 0)
        {
            return;
        }

        var targetW = previewFrame.Width;
        var targetH = previewFrame.Height;

        var scaleX = targetW / sourceW;
        var scaleY = targetH / sourceH;
        var scale = Math.Min(scaleX, scaleY);

        boundViewModel.ForegroundWidth = sourceW * scale;
        boundViewModel.ForegroundHeight = sourceH * scale;
    }
}
