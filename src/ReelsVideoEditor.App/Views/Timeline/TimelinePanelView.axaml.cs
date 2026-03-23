using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ReelsVideoEditor.App.DragDrop;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;
using ReelsVideoEditor.App.ViewModels.Timeline;
using System;

namespace ReelsVideoEditor.App.Views.Timeline;

public partial class TimelinePanelView : UserControl
{
    private Point? _dragStartPoint;
    private TimelineClipItem? _activeLevelClip;
    private bool _isAdjustingAudioLevel;
    private const double ClipTopEdgeThreshold = 6;
    private const double LevelHandleHitThreshold = 5;

    public TimelinePanelView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, TimelinePanelView_OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, TimelineSeekSurface_OnPointerMoved, RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, TimelineSeekSurface_OnPointerReleased, RoutingStrategies.Bubble);
    }

    private void TimelinePanelView_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TimelineViewModel viewModel) return;

        if (e.Key == Key.Delete)
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

    private void TimelineScrollViewer_OnPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
    {
        if (DataContext is TimelineViewModel viewModel && sender is ScrollViewer scrollViewer)
        {
            if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                viewModel.ChangeLaneHeightFromWheel(eventArgs.Delta.Y);
            }
            else
            {
                viewModel.ChangeZoomFromWheel(eventArgs.Delta.Y, scrollViewer.Bounds.Width);
            }

            eventArgs.Handled = true;
        }
    }

    private void TimelineTrack_OnDragOver(object? sender, DragEventArgs eventArgs)
    {
        eventArgs.DragEffects = TryGetClipPayload(eventArgs, out _, out _, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private void TimelineTrack_OnDrop(object? sender, DragEventArgs eventArgs)
    {
        if (DataContext is not TimelineViewModel viewModel || sender is not Control trackControl)
        {
            return;
        }

        if (!TryGetClipPayload(eventArgs, out var name, out var durationSeconds, out var path))
        {
            return;
        }

        var dropPositionX = eventArgs.GetPosition(trackControl).X;
        viewModel.AddClipFromExplorer(name, path, durationSeconds, dropPositionX);
        eventArgs.Handled = true;
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

        _dragStartPoint = eventArgs.GetPosition(timelineCanvas);

        if (viewModel.IsCutterToolActive)
        {
            viewModel.SeekCutterToCanvasPosition(_dragStartPoint.Value.X);

            if (IsPointerOverClip(eventArgs.Source))
            {
                viewModel.TryCutAtPlayhead();
            }

            eventArgs.Handled = true;
            return;
        }

        viewModel.SeekToPosition(_dragStartPoint.Value.X);

        viewModel.ClearSelection();
        eventArgs.Handled = true;
    }

    private static bool IsPointerOverClip(object? source)
    {
        if (source is not Visual sourceVisual)
        {
            return false;
        }

        if (sourceVisual is StyledElement sourceElement && sourceElement.DataContext is TimelineClipItem)
        {
            return true;
        }

        foreach (var ancestor in sourceVisual.GetVisualAncestors())
        {
            if (ancestor is StyledElement styledElement && styledElement.DataContext is TimelineClipItem)
            {
                return true;
            }
        }

        return false;
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

        viewModel.SelectClipsInBox(minX, maxX);
    }

    private void TimelineSeekSurface_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        _dragStartPoint = null;
        var selectionBox = this.FindControl<Border>("SelectionBox");
        if (selectionBox != null)
        {
            selectionBox.IsVisible = false;
        }
    }

    private void AudioClipLevel_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
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
}
