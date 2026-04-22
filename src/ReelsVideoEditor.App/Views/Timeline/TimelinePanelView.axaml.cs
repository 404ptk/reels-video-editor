using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ReelsVideoEditor.App.Models;
using ReelsVideoEditor.App.DragDrop;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;
using ReelsVideoEditor.App.ViewModels.Timeline;
using System;
using System.Linq;

namespace ReelsVideoEditor.App.Views.Timeline;

public partial class TimelinePanelView : UserControl
{
    private readonly Border? _timelineRuler;
    private Point? _dragStartPoint;
    private TimelineClipItem? _activeLevelClip;
    private bool _isAdjustingAudioLevel;
    private double? _lastDragOverCanvasX;
    private const double ClipTopEdgeThreshold = 6;
    private const double LevelHandleHitThreshold = 5;
    private const double ClipLeftInset = 10;

    public TimelinePanelView()
    {
        InitializeComponent();
        _timelineRuler = this.FindControl<Border>("TimelineRuler");
        InitializeScrollInfrastructure();

        AddHandler(KeyDownEvent, TimelinePanelView_OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, TimelinePanelView_OnPointerWheelChangedTunnel, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, TimelineSeekSurface_OnPointerMoved, RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, TimelineSeekSurface_OnPointerReleased, RoutingStrategies.Bubble);
    }

    private void TimelinePanelView_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TimelineViewModel viewModel) return;

        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.CopySelectedClip();
            e.Handled = true;
        }
        else if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (viewModel.PasteCopiedClipAtPlayhead())
            {
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete)
        {
            viewModel.DeleteSelectedClips();
            e.Handled = true;
        }
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.Undo();
            e.Handled = true;
        }
    }


    private void TimelineTrack_OnDragOver(object? sender, DragEventArgs eventArgs)
    {
        var hasTextPreset = TryGetTextPresetPayload(eventArgs, out var preset);
        var isSubtitlePreset = hasTextPreset && preset!.IsAutoCaptions;
        var hasSubtitles = isSubtitlePreset && DataContext is TimelineViewModel vm && vm.VideoLanes.Any(l => string.Equals(l.Label, "SUBTITLES", StringComparison.OrdinalIgnoreCase)) && vm.VideoClips.Any(c => string.Equals(c.VideoLaneLabel, "SUBTITLES", StringComparison.OrdinalIgnoreCase));

        var hasWatermarkPreset = TryGetWatermarkPresetPayload(eventArgs, out _);

        var hasPayload = TryGetClipPayload(eventArgs, out _, out _, out _)
            || (hasTextPreset && !hasSubtitles)
            || hasWatermarkPreset;
        eventArgs.DragEffects = hasPayload ? DragDropEffects.Copy : DragDropEffects.None;

        if (hasPayload)
        {
            var timelineCanvas = this.FindControl<Grid>("TimelineCanvas");
            if (timelineCanvas is not null)
            {
                _lastDragOverCanvasX = eventArgs.GetPosition(timelineCanvas).X;
            }
        }

        eventArgs.Handled = true;
    }

    private void TimelineTrack_OnDrop(object? sender, DragEventArgs eventArgs)
    {
        if (DataContext is not TimelineViewModel viewModel || sender is not Control trackControl)
        {
            return;
        }

        var timelineCanvas = this.FindControl<Grid>("TimelineCanvas");
        var referenceControl = timelineCanvas as Control ?? trackControl;
        var dropCanvasX = eventArgs.GetPosition(referenceControl).X;
        if (dropCanvasX <= 0 && _lastDragOverCanvasX.HasValue)
        {
            dropCanvasX = _lastDragOverCanvasX.Value;
        }

        var dropPositionX = Math.Max(0, dropCanvasX - ClipLeftInset);
        var targetLaneLabel = ResolveLaneLabel(trackControl);
        var handled = true;

        if (TryGetClipPayload(eventArgs, out var name, out var durationSeconds, out var path))
        {
            viewModel.AddClipFromExplorer(name, path, durationSeconds, dropPositionX, targetLaneLabel);
        }
        else if (TryGetTextPresetPayload(eventArgs, out var preset))
        {
            var isSubtitlePreset = preset.IsAutoCaptions;
            var hasSubtitles = isSubtitlePreset && viewModel.VideoLanes.Any(l => string.Equals(l.Label, "SUBTITLES", StringComparison.OrdinalIgnoreCase)) && viewModel.VideoClips.Any(c => string.Equals(c.VideoLaneLabel, "SUBTITLES", StringComparison.OrdinalIgnoreCase));

            if (!hasSubtitles)
            {
                viewModel.AddTextPresetClip(preset, dropPositionX, targetLaneLabel);
            }
        }
        else if (TryGetWatermarkPresetPayload(eventArgs, out var watermarkPreset))
        {
            viewModel.AddWatermarkPresetClip(watermarkPreset, dropPositionX, targetLaneLabel);
        }
        else
        {
            handled = false;
        }

        if (handled)
        {
            _lastDragOverCanvasX = null;
            eventArgs.Handled = true;
        }
    }

    private void VideoLaneHeader_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (_isAdjustingAudioLevel)
        {
            return;
        }

        if (DataContext is not TimelineViewModel viewModel
            || sender is not Control control
            || control.DataContext is not VideoLaneItem lane)
        {
            return;
        }

        var point = eventArgs.GetCurrentPoint(control);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        viewModel.SelectClipsInLane(lane.Label);
        eventArgs.Handled = true;
    }

    private void VideoClip_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (_isAdjustingAudioLevel)
        {
            return;
        }

        if (DataContext is not TimelineViewModel viewModel
            || sender is not Control control
            || control.DataContext is not TimelineClipItem clip)
        {
            return;
        }

        var point = eventArgs.GetCurrentPoint(control);
        
        if (point.Properties.IsRightButtonPressed)
        {
            viewModel.SelectSingleVideoClip(clip);
            return;
        }
        
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var timelineCanvas = this.FindControl<Grid>("TimelineCanvas");
        if (timelineCanvas is null)
        {
            return;
        }

        var localPosition = eventArgs.GetPosition(control);
        var fadeCorner = ResolveClipFadeCorner(control, clip, localPosition);
        if (fadeCorner is not ClipFadeCorner.None)
        {
            StartVideoClipFade(viewModel, clip, fadeCorner);
            eventArgs.Handled = true;
            return;
        }

        var resizeEdge = ResolveClipResizeEdge(control, localPosition.X);
        if (resizeEdge is not ClipResizeEdge.None)
        {
            StartVideoClipResize(viewModel, clip, resizeEdge);
            eventArgs.Handled = true;
            return;
        }

        StartVideoClipDrag(viewModel, timelineCanvas, clip, eventArgs);
        eventArgs.Handled = true;
    }

    private void VideoClip_OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (sender is Control hoverControl
            && DataContext is TimelineViewModel hoverViewModel
            && hoverControl.DataContext is TimelineClipItem hoverClip)
        {
            UpdateVideoClipResizeCursor(hoverViewModel, hoverControl, hoverClip, eventArgs);
        }

        if (_isAdjustingVideoFade
            && DataContext is TimelineViewModel fadeViewModel
            && sender is Control fadeControl
            && fadeControl.DataContext is TimelineClipItem fadeClip
            && ReferenceEquals(_fadingVideoClip, fadeClip))
        {
            var fadePoint = eventArgs.GetCurrentPoint(fadeControl);
            if (!fadePoint.Properties.IsLeftButtonPressed)
            {
                return;
            }

            var localPosition = eventArgs.GetPosition(fadeControl);
            UpdateVideoClipFadeFromLocalPosition(fadeViewModel, fadeClip, fadeControl, localPosition.X);
            eventArgs.Handled = true;
            return;
        }

        if (_isResizingVideoClip || _isAdjustingVideoFade)
        {
            return;
        }

        if (!_isDraggingVideoClip
            || DataContext is not TimelineViewModel viewModel
            || sender is not Control control
            || control.DataContext is not TimelineClipItem clip
            || !ReferenceEquals(_draggingVideoClip, clip))
        {
            return;
        }

        var point = eventArgs.GetCurrentPoint(control);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var timelineCanvas = this.FindControl<Grid>("TimelineCanvas");
        if (timelineCanvas is null)
        {
            return;
        }

        var pointerCanvasX = eventArgs.GetPosition(timelineCanvas).X;
        var pointerSeconds = (pointerCanvasX - ClipLeftInset) / Math.Max(0.0001, viewModel.TickWidth);
        var nextStartSeconds = pointerSeconds - _draggingClipPointerOffsetSeconds;
        var snappingEnabled = !eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift);

        var targetLaneLabel = ResolveLaneLabelFromPointer(viewModel, eventArgs);
        viewModel.MoveClipToStart(clip, nextStartSeconds, targetLaneLabel, snappingEnabled);
        eventArgs.Handled = true;
    }

    private void VideoClip_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (_isResizingVideoClip
            && DataContext is TimelineViewModel resizeViewModel
            && _resizingVideoClip is TimelineClipItem resizingClip)
        {
            EndVideoClipResize(resizingClip, resizeViewModel, commit: true);
            eventArgs.Handled = true;
            return;
        }

        if (_isAdjustingVideoFade
            && DataContext is TimelineViewModel fadeViewModel
            && _fadingVideoClip is TimelineClipItem fadingClip)
        {
            EndVideoClipFade(fadingClip, fadeViewModel, commit: true);
            eventArgs.Handled = true;
            return;
        }

        if (!_isDraggingVideoClip
            || DataContext is not TimelineViewModel viewModel
            || sender is not Control control
            || control.DataContext is not TimelineClipItem clip
            || !ReferenceEquals(_draggingVideoClip, clip))
        {
            return;
        }

        EndVideoClipDrag(clip, viewModel, commit: true);
        eventArgs.Handled = true;
    }

    private void VideoClip_OnPointerExited(object? sender, PointerEventArgs eventArgs)
    {
        if (sender is not Control control)
        {
            return;
        }

        control.Cursor = null;
        if (_isDraggingVideoClip || _isResizingVideoClip || _isAdjustingVideoFade)
        {
            return;
        }

        if (DataContext is TimelineViewModel viewModel)
        {
            ClearTrimMarker(viewModel);
        }
    }

    private void TimelineSeekSurface_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (_isAdjustingAudioLevel)
        {
            return;
        }

        if (DataContext is not TimelineViewModel viewModel || sender is not Control seekSurface)
        {
            return;
        }

        var currentPoint = eventArgs.GetCurrentPoint(seekSurface);
        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();

        var timelineCanvas = this.FindControl<Grid>("TimelineCanvas");
        if (timelineCanvas == null) return;

        var pointerOnCanvas = eventArgs.GetPosition(timelineCanvas);
        var isRulerClick = ReferenceEquals(sender, _timelineRuler);
        _dragStartPoint = isRulerClick ? null : pointerOnCanvas;

        if (viewModel.IsCutterToolActive)
        {
            viewModel.SeekCutterToCanvasPosition(pointerOnCanvas.X);

            viewModel.TryCutAtPlayhead();

            eventArgs.Handled = true;
            return;
        }

        viewModel.SeekToPosition(pointerOnCanvas.X);

        viewModel.ClearSelection();
        eventArgs.Handled = true;
    }

    private void TimelineCanvas_OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (sender is not Control canvas || DataContext is not TimelineViewModel viewModel)
        {
            return;
        }

        var pointerX = eventArgs.GetPosition(canvas).X;
        viewModel.UpdateCutterMarkerFromCanvas(pointerX, visible: true);
    }

    private void TimelineCanvas_OnPointerEntered(object? sender, PointerEventArgs eventArgs)
    {
        if (sender is not Control canvas || DataContext is not TimelineViewModel viewModel)
        {
            return;
        }

        var pointerX = eventArgs.GetPosition(canvas).X;
        viewModel.UpdateCutterMarkerFromCanvas(pointerX, visible: true);
    }

    private void TimelineCanvas_OnPointerExited(object? sender, PointerEventArgs eventArgs)
    {
        if (DataContext is TimelineViewModel viewModel)
        {
            viewModel.HideCutterMarker();
        }
    }

    private void TimelineSeekSurface_OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (_isAdjustingAudioLevel)
        {
            return;
        }

        if (_isResizingVideoClip)
        {
            if (DataContext is TimelineViewModel resizeViewModel && _resizingVideoClip is TimelineClipItem resizingClip)
            {
                var timelineCanvasForResize = this.FindControl<Grid>("TimelineCanvas");
                if (timelineCanvasForResize is not null)
                {
                    var resizePointerPoint = eventArgs.GetCurrentPoint(sender as Visual ?? this);
                    if (!resizePointerPoint.Properties.IsLeftButtonPressed)
                    {
                        return;
                    }

                    var pointerCanvasX = eventArgs.GetPosition(timelineCanvasForResize).X;
                    var pointerSeconds = (pointerCanvasX - ClipLeftInset) / Math.Max(0.0001, resizeViewModel.TickWidth);

                    if (_activeResizeEdge == ClipResizeEdge.Left)
                    {
                        resizeViewModel.ResizeClipFromLeft(resizingClip, pointerSeconds);
                    }
                    else if (_activeResizeEdge == ClipResizeEdge.Right)
                    {
                        resizeViewModel.ResizeClipFromRight(resizingClip, pointerSeconds);
                    }
                }
            }

            return;
        }

        if (_isAdjustingVideoFade)
        {
            if (DataContext is TimelineViewModel fadeViewModel && _fadingVideoClip is TimelineClipItem fadingClip)
            {
                var timelineCanvasForFade = this.FindControl<Grid>("TimelineCanvas");
                if (timelineCanvasForFade is not null)
                {
                    var fadePointerPoint = eventArgs.GetCurrentPoint(sender as Visual ?? this);
                    if (!fadePointerPoint.Properties.IsLeftButtonPressed)
                    {
                        return;
                    }

                    var pointerCanvasX = eventArgs.GetPosition(timelineCanvasForFade).X;
                    var pointerSeconds = (pointerCanvasX - ClipLeftInset) / Math.Max(0.0001, fadeViewModel.TickWidth);
                    UpdateVideoClipFadeFromPointer(fadeViewModel, fadingClip, pointerSeconds);
                }
            }

            return;
        }

        if (_isDraggingVideoClip)
        {
            if (DataContext is TimelineViewModel dragViewModel && _draggingVideoClip is TimelineClipItem draggingClip)
            {
                var timelineCanvasForDrag = this.FindControl<Grid>("TimelineCanvas");
                if (timelineCanvasForDrag is not null)
                {
                    var dragPointerPoint = eventArgs.GetCurrentPoint(sender as Visual ?? this);
                    if (!dragPointerPoint.Properties.IsLeftButtonPressed)
                    {
                        return;
                    }
                    else
                    {
                        var pointerCanvasX = eventArgs.GetPosition(timelineCanvasForDrag).X;
                        var pointerSeconds = (pointerCanvasX - ClipLeftInset) / Math.Max(0.0001, dragViewModel.TickWidth);
                        var nextStartSeconds = pointerSeconds - _draggingClipPointerOffsetSeconds;
                        var snappingEnabled = !eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift);
                        var targetLaneLabel = ResolveLaneLabelFromPointer(dragViewModel, eventArgs);
                        dragViewModel.MoveClipToStart(draggingClip, nextStartSeconds, targetLaneLabel, snappingEnabled);
                    }
                }
            }

            return;
        }

        if (_dragStartPoint == null || DataContext is not TimelineViewModel viewModel || sender is not Control control)
            return;
            
        var timelineCanvas = this.FindControl<Grid>("TimelineCanvas");
        if (timelineCanvas == null) return;

        var currentPoint = eventArgs.GetCurrentPoint(control);
        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = null;
            return;
        }

        var currentPosition = eventArgs.GetPosition(timelineCanvas);
        var minX = Math.Min(_dragStartPoint.Value.X, currentPosition.X);
        var maxX = Math.Max(_dragStartPoint.Value.X, currentPosition.X);
        var width = maxX - minX;

        var minY = Math.Min(_dragStartPoint.Value.Y, currentPosition.Y);
        var maxY = Math.Max(_dragStartPoint.Value.Y, currentPosition.Y);
        var height = maxY - minY;

        var selectionBox = this.FindControl<Border>("SelectionBox");
        if (selectionBox != null)
        {
            selectionBox.IsVisible = true;
            selectionBox.Margin = new Thickness(minX, minY, 0, 0);
            selectionBox.Width = width;
            selectionBox.Height = height;
        }

        viewModel.SelectClipsInBox(minX, maxX, minY, maxY);
    }

    private void TimelineSeekSurface_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (_isResizingVideoClip
            && DataContext is TimelineViewModel resizeViewModel
            && _resizingVideoClip is TimelineClipItem resizingClip)
        {
            EndVideoClipResize(resizingClip, resizeViewModel, commit: true);
        }

        if (_isAdjustingVideoFade
            && DataContext is TimelineViewModel fadeViewModel
            && _fadingVideoClip is TimelineClipItem fadingClip)
        {
            EndVideoClipFade(fadingClip, fadeViewModel, commit: true);
        }

        if (_isDraggingVideoClip
            && DataContext is TimelineViewModel viewModel
            && _draggingVideoClip is TimelineClipItem clip)
        {
            EndVideoClipDrag(clip, viewModel, commit: true);
        }

        _dragStartPoint = null;
        var selectionBox = this.FindControl<Border>("SelectionBox");
        if (selectionBox != null)
        {
            selectionBox.IsVisible = false;
        }
    }

    private void AudioClipLevel_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is Control control && control.DataContext is TimelineClipItem clip)
        {
            var point = eventArgs.GetCurrentPoint(control);
            
            if (point.Properties.IsRightButtonPressed)
            {
                if (DataContext is TimelineViewModel viewModel)
                {
                    viewModel.SelectSingleVideoClip(clip);
                }
                return;
            }
        }
        
        BeginAudioClipLevelAdjustment(sender, eventArgs);
    }

    private void AudioClipLevel_OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        UpdateAudioClipLevelAdjustment(sender, eventArgs);
    }

    private void ClipLevel_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        EndClipLevelAdjustment(sender as Control);
    }

    private void BeginAudioClipLevelAdjustment(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not Control control || control.DataContext is not TimelineClipItem clip)
        {
            return;
        }

        var point = eventArgs.GetCurrentPoint(control);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var localPosition = eventArgs.GetPosition(control);
        var onTopEdge = localPosition.Y <= ClipTopEdgeThreshold;
        var onLevelLine = IsPointerOnAudioLevelLine(clip, localPosition.Y);
        if (!onTopEdge && !onLevelLine)
        {
            return;
        }

        _dragStartPoint = null;
        _activeLevelClip = clip;
        _isAdjustingAudioLevel = true;

        control.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        eventArgs.Handled = true;
    }

    private void UpdateAudioClipLevelAdjustment(object? sender, PointerEventArgs eventArgs)
    {
        if (sender is not Control control || control.DataContext is not TimelineClipItem clip)
        {
            return;
        }

        if (!_isAdjustingAudioLevel || _activeLevelClip != clip)
        {
            var hoverPosition = eventArgs.GetPosition(control);
            var onTopEdge = hoverPosition.Y <= ClipTopEdgeThreshold;
            var onLevelLine = IsPointerOnAudioLevelLine(clip, hoverPosition.Y);
            control.Cursor = onTopEdge || onLevelLine ? new Cursor(StandardCursorType.SizeNorthSouth) : null;
            return;
        }

        var currentPoint = eventArgs.GetCurrentPoint(control);
        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            EndClipLevelAdjustment(control);
            return;
        }

        var localY = eventArgs.GetPosition(control).Y;
        var drawableHeight = Math.Max(1.0, control.Bounds.Height);
        var nextLevel = Math.Clamp(1.0 - (localY / drawableHeight), 0.0, 1.0);

        if (DataContext is TimelineViewModel timelineViewModel)
        {
            timelineViewModel.SetAudioClipVolume(clip, nextLevel);
        }
        else
        {
            clip.VolumeLevel = nextLevel;
        }

        eventArgs.Handled = true;
    }

    private void EndClipLevelAdjustment(Control? control)
    {
        _activeLevelClip = null;
        _isAdjustingAudioLevel = false;

        if (control != null)
        {
            control.Cursor = null;
        }
    }

    private static bool IsPointerOnAudioLevelLine(TimelineClipItem clip, double localY)
    {
        if (!clip.IsAudioLevelLineVisible)
        {
            return false;
        }

        return Math.Abs(localY - clip.AudioLevelLineTop) <= LevelHandleHitThreshold;
    }


    private static bool TryGetClipPayload(DragEventArgs eventArgs, out string name, out double durationSeconds, out string path)
    {
        name = string.Empty;
        durationSeconds = 0;
        path = string.Empty;

#pragma warning disable CS0618
        var data = eventArgs.Data;
#pragma warning restore CS0618
        if (!data.Contains(VideoClipDragPayload.Format))
        {
            return false;
        }

        var rawPayload = data.Get(VideoClipDragPayload.Format);
        var parsed = VideoClipDragPayload.TryParse(rawPayload, out path, out name, out durationSeconds);
        return parsed;
    }

    private static bool TryGetTextPresetPayload(DragEventArgs eventArgs, out TextPresetDefinition preset)
    {
        preset = null!;

#pragma warning disable CS0618
        var data = eventArgs.Data;
#pragma warning restore CS0618
        if (!data.Contains(TextPresetDragPayload.Format))
        {
            return false;
        }

        var rawPayload = data.Get(TextPresetDragPayload.Format);
        var parsed = TextPresetDragPayload.TryParse(rawPayload, out var parsedPreset);
        if (!parsed || parsedPreset is null)
        {
            return false;
        }

        preset = parsedPreset;
        return true;
    }

    private static bool TryGetWatermarkPresetPayload(DragEventArgs eventArgs, out WatermarkPresetDefinition preset)
    {
        preset = null!;

#pragma warning disable CS0618
        var data = eventArgs.Data;
#pragma warning restore CS0618
        if (!data.Contains(WatermarkPresetDragPayload.Format))
        {
            return false;
        }

        var rawPayload = data.Get(WatermarkPresetDragPayload.Format);
        var parsed = WatermarkPresetDragPayload.TryParse(rawPayload, out var parsedPreset);
        if (!parsed || parsedPreset is null)
        {
            return false;
        }

        preset = parsedPreset;
        return true;
    }

    private static string? ResolveLaneLabel(Control control)
    {
        if (control.DataContext is VideoLaneItem lane)
        {
            return lane.Label;
        }

        foreach (var ancestor in control.GetVisualAncestors())
        {
            if (ancestor is StyledElement styled && styled.DataContext is VideoLaneItem ancestorLane)
            {
                return ancestorLane.Label;
            }
        }

        return null;
    }

}
