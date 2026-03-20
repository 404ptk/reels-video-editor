using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ReelsVideoEditor.App.DragDrop;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;
using ReelsVideoEditor.App.ViewModels.Timeline;
using System;

namespace ReelsVideoEditor.App.Views.Timeline;

public partial class TimelinePanelView : UserControl
{
    private Point? _dragStartPoint;
    private TimelineClipItem? _activeLevelClip;
    private bool _isAdjustingVideoLevel;
    private bool _isAdjustingAudioLevel;
    private double _levelAdjustStartY;
    private double _levelAdjustStartValue;
    private const double ClipTopEdgeThreshold = 6;
    private const double LevelHandleHitThreshold = 5;
    private const double PixelsPerLevelStep = 120;

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
        if (_isAdjustingVideoLevel || _isAdjustingAudioLevel)
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
        viewModel.ClearSelection();

        viewModel.SeekToPosition(_dragStartPoint.Value.X);
        eventArgs.Handled = true;
    }

    private void TimelineSeekSurface_OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (_isAdjustingVideoLevel || _isAdjustingAudioLevel)
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

    private void VideoClipLevel_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        BeginClipLevelAdjustment(sender, eventArgs, isVideoClip: true);
    }

    private void AudioClipLevel_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        BeginClipLevelAdjustment(sender, eventArgs, isVideoClip: false);
    }

    private void VideoClipLevel_OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        UpdateClipLevelAdjustment(sender, eventArgs, isVideoClip: true);
    }

    private void AudioClipLevel_OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        UpdateClipLevelAdjustment(sender, eventArgs, isVideoClip: false);
    }

    private void ClipLevel_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        EndClipLevelAdjustment(sender as Control);
    }

    private void BeginClipLevelAdjustment(object? sender, PointerPressedEventArgs eventArgs, bool isVideoClip)
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
        var onLevelLine = IsPointerOnLevelLine(clip, localPosition.Y, isVideoClip);
        if (!onTopEdge && !onLevelLine)
        {
            return;
        }

        _dragStartPoint = null;
        _activeLevelClip = clip;
        _isAdjustingVideoLevel = isVideoClip;
        _isAdjustingAudioLevel = !isVideoClip;
        _levelAdjustStartY = eventArgs.GetPosition(this).Y;
        _levelAdjustStartValue = isVideoClip ? clip.OpacityLevel : clip.VolumeLevel;

        control.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        eventArgs.Handled = true;
    }

    private void UpdateClipLevelAdjustment(object? sender, PointerEventArgs eventArgs, bool isVideoClip)
    {
        if (sender is not Control control || control.DataContext is not TimelineClipItem clip)
        {
            return;
        }

        var isAdjusting = isVideoClip ? _isAdjustingVideoLevel : _isAdjustingAudioLevel;
        if (!isAdjusting || _activeLevelClip != clip)
        {
            var hoverPosition = eventArgs.GetPosition(control);
            var onTopEdge = hoverPosition.Y <= ClipTopEdgeThreshold;
            var onLevelLine = IsPointerOnLevelLine(clip, hoverPosition.Y, isVideoClip);
            control.Cursor = onTopEdge || onLevelLine ? new Cursor(StandardCursorType.SizeNorthSouth) : null;
            return;
        }

        var currentPoint = eventArgs.GetCurrentPoint(control);
        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            EndClipLevelAdjustment(control);
            return;
        }

        var deltaY = eventArgs.GetPosition(this).Y - _levelAdjustStartY;
        var nextLevel = Math.Clamp(_levelAdjustStartValue - (deltaY / PixelsPerLevelStep), 0.0, 1.0);

        if (DataContext is TimelineViewModel timelineViewModel)
        {
            if (isVideoClip)
            {
                timelineViewModel.SetVideoClipOpacity(clip, nextLevel);
            }
            else
            {
                timelineViewModel.SetAudioClipVolume(clip, nextLevel);
            }
        }
        else if (isVideoClip)
        {
            clip.OpacityLevel = nextLevel;
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
        _isAdjustingVideoLevel = false;
        _isAdjustingAudioLevel = false;

        if (control != null)
        {
            control.Cursor = null;
        }
    }

    private static bool IsPointerOnLevelLine(TimelineClipItem clip, double localY, bool isVideoClip)
    {
        var isVisible = isVideoClip ? clip.IsVideoLevelLineVisible : clip.IsAudioLevelLineVisible;
        if (!isVisible)
        {
            return false;
        }

        var lineTop = isVideoClip ? clip.VideoLevelLineTop : clip.AudioLevelLineTop;
        return Math.Abs(localY - lineTop) <= LevelHandleHitThreshold;
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
