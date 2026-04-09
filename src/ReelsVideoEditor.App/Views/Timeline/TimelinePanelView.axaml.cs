using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private enum ClipResizeEdge
    {
        None,
        Left,
        Right
    }

    private readonly Border? _timelineRuler;
    private readonly ScrollViewer? _laneHeaderScrollViewer;
    private readonly ScrollViewer? _timelineScrollViewer;
    private readonly ScrollBar? _timelineVerticalScrollBar;
    private Point? _dragStartPoint;
    private TimelineClipItem? _activeLevelClip;
    private TimelineClipItem? _draggingVideoClip;
    private TimelineClipItem? _resizingVideoClip;
    private Guid? _activeTrimMarkerLinkId;
    private bool _isAdjustingAudioLevel;
    private bool _isDraggingVideoClip;
    private bool _isResizingVideoClip;
    private ClipResizeEdge _activeResizeEdge;
    private double _draggingClipInitialStartSeconds;
    private string _draggingClipInitialLaneLabel = string.Empty;
    private double _draggingClipPointerOffsetSeconds;
    private double _resizingClipInitialStartSeconds;
    private double _resizingClipInitialDurationSeconds;
    private double _resizingClipInitialSourceStartSeconds;
    private double? _lastDragOverCanvasX;
    private const double ClipTopEdgeThreshold = 6;
    private const double LevelHandleHitThreshold = 5;
    private const double ClipHorizontalResizeEdgeThreshold = 8;
    private const double ClipLeftInset = 10;
    private const double MouseWheelVerticalStep = 48;
    private bool _isSyncingVerticalScroll;

    public TimelinePanelView()
    {
        InitializeComponent();
        _timelineRuler = this.FindControl<Border>("TimelineRuler");
        _laneHeaderScrollViewer = this.FindControl<ScrollViewer>("LaneHeaderScrollViewer");
        _timelineScrollViewer = this.FindControl<ScrollViewer>("TimelineScrollViewer");
        _timelineVerticalScrollBar = this.FindControl<ScrollBar>("TimelineVerticalScrollBar");

        if (_timelineVerticalScrollBar is not null)
        {
            _timelineVerticalScrollBar.PropertyChanged += TimelineVerticalScrollBar_OnPropertyChanged;
        }

        AttachedToVisualTree += (_, _) => UpdateVerticalScrollBarMetrics();
        SizeChanged += (_, _) => UpdateVerticalScrollBarMetrics();
        LayoutUpdated += (_, _) => UpdateVerticalScrollBarMetrics();

        UpdateVerticalScrollBarMetrics();

        AddHandler(KeyDownEvent, TimelinePanelView_OnKeyDown, RoutingStrategies.Tunnel);
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

    private void TimelineScrollViewer_OnPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
    {
        if (DataContext is TimelineViewModel viewModel)
        {
            if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                viewModel.ChangeLaneHeightFromWheel(eventArgs.Delta.Y);
                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                var viewportWidth = _timelineScrollViewer?.Bounds.Width ?? (sender as ScrollViewer)?.Bounds.Width ?? Bounds.Width;
                viewModel.ChangeZoomFromWheel(eventArgs.Delta.Y, viewportWidth);
                eventArgs.Handled = true;
                return;
            }

            var currentOffsetY = _timelineScrollViewer?.Offset.Y ?? _laneHeaderScrollViewer?.Offset.Y ?? 0;
            var targetOffsetY = currentOffsetY - (eventArgs.Delta.Y * MouseWheelVerticalStep);
            SetVerticalOffset(targetOffsetY);
            eventArgs.Handled = true;
        }
    }

    private void TimelineScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs eventArgs)
    {
        if (_isSyncingVerticalScroll)
        {
            return;
        }

        UpdateVerticalScrollBarMetrics();
        var offsetY = _timelineScrollViewer?.Offset.Y ?? 0;
        SetVerticalOffset(offsetY);
    }

    private void LaneHeaderScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs eventArgs)
    {
        if (_isSyncingVerticalScroll)
        {
            return;
        }

        UpdateVerticalScrollBarMetrics();
        var offsetY = _laneHeaderScrollViewer?.Offset.Y ?? 0;
        SetVerticalOffset(offsetY);
    }

    private void TimelineVerticalScrollBar_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (_isSyncingVerticalScroll || _timelineVerticalScrollBar is null)
        {
            return;
        }

        if (eventArgs.Property == RangeBase.ValueProperty)
        {
            SetVerticalOffset(_timelineVerticalScrollBar.Value);
        }
    }

    private void UpdateVerticalScrollBarMetrics()
    {
        if (_timelineScrollViewer is null || _timelineVerticalScrollBar is null)
        {
            return;
        }

        var maxOffset = Math.Max(0, _timelineScrollViewer.Extent.Height - _timelineScrollViewer.Viewport.Height);
        _timelineVerticalScrollBar.Minimum = 0;
        _timelineVerticalScrollBar.Maximum = maxOffset;
        _timelineVerticalScrollBar.ViewportSize = _timelineScrollViewer.Viewport.Height;
        _timelineVerticalScrollBar.SmallChange = MouseWheelVerticalStep;
        _timelineVerticalScrollBar.LargeChange = Math.Max(MouseWheelVerticalStep * 2, _timelineScrollViewer.Viewport.Height * 0.9);
        _timelineVerticalScrollBar.IsVisible = maxOffset > 0;
    }

    private void SetVerticalOffset(double targetOffsetY)
    {
        var maxOffset = 0.0;
        if (_timelineScrollViewer is not null)
        {
            maxOffset = Math.Max(maxOffset, _timelineScrollViewer.Extent.Height - _timelineScrollViewer.Viewport.Height);
        }

        if (_laneHeaderScrollViewer is not null)
        {
            maxOffset = Math.Max(maxOffset, _laneHeaderScrollViewer.Extent.Height - _laneHeaderScrollViewer.Viewport.Height);
        }

        var clampedOffset = Math.Clamp(targetOffsetY, 0, Math.Max(0, maxOffset));

        _isSyncingVerticalScroll = true;
        try
        {
            if (_laneHeaderScrollViewer is not null)
            {
                _laneHeaderScrollViewer.Offset = new Vector(_laneHeaderScrollViewer.Offset.X, clampedOffset);
            }

            if (_timelineScrollViewer is not null)
            {
                _timelineScrollViewer.Offset = new Vector(_timelineScrollViewer.Offset.X, clampedOffset);
            }

            if (_timelineVerticalScrollBar is not null)
            {
                _timelineVerticalScrollBar.Value = clampedOffset;
            }
        }
        finally
        {
            _isSyncingVerticalScroll = false;
        }
    }

    private void TimelineTrack_OnDragOver(object? sender, DragEventArgs eventArgs)
    {
        var hasTextPreset = TryGetTextPresetPayload(eventArgs, out var preset);
        var isSubtitlePreset = hasTextPreset && preset!.IsAutoCaptions;
        var hasSubtitles = isSubtitlePreset && DataContext is TimelineViewModel vm && vm.VideoLanes.Any(l => string.Equals(l.Label, "SUBTITLES", StringComparison.OrdinalIgnoreCase));

        var hasPayload = TryGetClipPayload(eventArgs, out _, out _, out _)
            || (hasTextPreset && !hasSubtitles);
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

        if (TryGetClipPayload(eventArgs, out var name, out var durationSeconds, out var path))
        {
            var timelineCanvas = this.FindControl<Grid>("TimelineCanvas");
            var referenceControl = timelineCanvas as Control ?? trackControl;
            var dropCanvasX = eventArgs.GetPosition(referenceControl).X;
            if (dropCanvasX <= 0 && _lastDragOverCanvasX.HasValue)
            {
                dropCanvasX = _lastDragOverCanvasX.Value;
            }

            var dropPositionX = Math.Max(0, dropCanvasX - ClipLeftInset);
            var targetLaneLabel = ResolveLaneLabel(trackControl);
            viewModel.AddClipFromExplorer(name, path, durationSeconds, dropPositionX, targetLaneLabel);
            _lastDragOverCanvasX = null;
            eventArgs.Handled = true;
            return;
        }

        if (TryGetTextPresetPayload(eventArgs, out var preset))
        {
            var isSubtitlePreset = preset.IsAutoCaptions;
            var hasSubtitles = isSubtitlePreset && viewModel.VideoLanes.Any(l => string.Equals(l.Label, "SUBTITLES", StringComparison.OrdinalIgnoreCase));

            if (hasSubtitles)
            {
                eventArgs.Handled = true;
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
            viewModel.AddTextPresetClip(preset, dropPositionX, targetLaneLabel);
            _lastDragOverCanvasX = null;
            eventArgs.Handled = true;
        }
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

        if (_isResizingVideoClip)
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
        if (_isDraggingVideoClip || _isResizingVideoClip)
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

    private void EndVideoClipDrag(TimelineClipItem clip, TimelineViewModel viewModel, bool commit)
    {
        var previousStartSeconds = _draggingClipInitialStartSeconds;
        var previousLaneLabel = _draggingClipInitialLaneLabel;

        if (commit)
        {
            viewModel.CommitClipMove(clip, previousStartSeconds, previousLaneLabel);
        }

        _isDraggingVideoClip = false;
        _draggingVideoClip = null;
        _draggingClipPointerOffsetSeconds = 0;
        _draggingClipInitialStartSeconds = 0;
        _draggingClipInitialLaneLabel = string.Empty;
        ClearTrimMarker(viewModel);
    }

    private void StartVideoClipResize(TimelineViewModel viewModel, TimelineClipItem clip, ClipResizeEdge resizeEdge)
    {
        _resizingVideoClip = clip;
        _activeResizeEdge = resizeEdge;
        _isResizingVideoClip = true;
        _dragStartPoint = null;

        _resizingClipInitialStartSeconds = clip.StartSeconds;
        _resizingClipInitialDurationSeconds = clip.DurationSeconds;
        _resizingClipInitialSourceStartSeconds = clip.SourceStartSeconds;
        SetTrimMarker(viewModel, clip, resizeEdge);

        viewModel.SelectSingleVideoClip(clip);
    }

    private void EndVideoClipResize(TimelineClipItem clip, TimelineViewModel viewModel, bool commit)
    {
        var previousStartSeconds = _resizingClipInitialStartSeconds;
        var previousDurationSeconds = _resizingClipInitialDurationSeconds;
        var previousSourceStartSeconds = _resizingClipInitialSourceStartSeconds;

        if (commit)
        {
            viewModel.CommitClipResize(clip, previousStartSeconds, previousDurationSeconds, previousSourceStartSeconds);
        }

        _isResizingVideoClip = false;
        _resizingVideoClip = null;
        _activeResizeEdge = ClipResizeEdge.None;
        _resizingClipInitialStartSeconds = 0;
        _resizingClipInitialDurationSeconds = 0;
        _resizingClipInitialSourceStartSeconds = 0;
        ClearTrimMarker(viewModel);
    }

    private void StartVideoClipDrag(TimelineViewModel viewModel, Control timelineCanvas, TimelineClipItem clip, PointerPressedEventArgs eventArgs)
    {
        var pointerCanvasX = eventArgs.GetPosition(timelineCanvas).X;
        var clipLeftInCanvas = ClipLeftInset + clip.Left;
        _draggingClipPointerOffsetSeconds = Math.Clamp(
            (pointerCanvasX - clipLeftInCanvas) / Math.Max(0.0001, viewModel.TickWidth),
            0,
            clip.DurationSeconds);

        _draggingVideoClip = clip;
        _draggingClipInitialStartSeconds = clip.StartSeconds;
        _draggingClipInitialLaneLabel = clip.VideoLaneLabel;
        _dragStartPoint = null;
        _isDraggingVideoClip = true;

        viewModel.SelectSingleVideoClip(clip);
    }

    private static bool IsPointerOnAudioLevelLine(TimelineClipItem clip, double localY)
    {
        if (!clip.IsAudioLevelLineVisible)
        {
            return false;
        }

        return Math.Abs(localY - clip.AudioLevelLineTop) <= LevelHandleHitThreshold;
    }

    private static ClipResizeEdge ResolveClipResizeEdge(Control control, double localX)
    {
        var threshold = Math.Min(ClipHorizontalResizeEdgeThreshold, Math.Max(2, control.Bounds.Width / 3));
        if (localX <= threshold)
        {
            return ClipResizeEdge.Left;
        }

        if (localX >= control.Bounds.Width - threshold)
        {
            return ClipResizeEdge.Right;
        }

        return ClipResizeEdge.None;
    }

    private void UpdateVideoClipResizeCursor(TimelineViewModel viewModel, Control control, TimelineClipItem clip, PointerEventArgs eventArgs)
    {
        if (_isDraggingVideoClip)
        {
            return;
        }

        if (_isResizingVideoClip)
        {
            SetTrimMarker(viewModel, clip, _activeResizeEdge);
            control.Cursor = _activeResizeEdge is ClipResizeEdge.None ? null : new Cursor(StandardCursorType.SizeWestEast);
            return;
        }

        var localPosition = eventArgs.GetPosition(control);
        var resizeEdge = ResolveClipResizeEdge(control, localPosition.X);
        control.Cursor = resizeEdge is ClipResizeEdge.None
            ? null
            : new Cursor(StandardCursorType.SizeWestEast);

        SetTrimMarker(viewModel, clip, resizeEdge);
    }

    private void SetTrimMarker(TimelineViewModel viewModel, TimelineClipItem clip, ClipResizeEdge edge)
    {
        var showLeft = edge == ClipResizeEdge.Left;
        var showRight = edge == ClipResizeEdge.Right;

        if (!showLeft && !showRight)
        {
            ClearTrimMarker(viewModel);
            return;
        }

        if (_activeTrimMarkerLinkId.HasValue && _activeTrimMarkerLinkId.Value != clip.LinkId)
        {
            ClearTrimMarker(viewModel);
        }

        clip.IsLeftTrimMarkerVisible = showLeft;
        clip.IsRightTrimMarkerVisible = showRight;

        var linkedAudio = viewModel.AudioClips.FirstOrDefault(audio => audio.LinkId == clip.LinkId);
        if (linkedAudio is not null)
        {
            linkedAudio.IsLeftTrimMarkerVisible = showLeft;
            linkedAudio.IsRightTrimMarkerVisible = showRight;
        }

        _activeTrimMarkerLinkId = clip.LinkId;
    }

    private void ClearTrimMarker(TimelineViewModel viewModel)
    {
        if (!_activeTrimMarkerLinkId.HasValue)
        {
            return;
        }

        var linkId = _activeTrimMarkerLinkId.Value;
        foreach (var candidate in viewModel.VideoClips)
        {
            if (candidate.LinkId != linkId)
            {
                continue;
            }

            candidate.IsLeftTrimMarkerVisible = false;
            candidate.IsRightTrimMarkerVisible = false;
        }

        foreach (var candidate in viewModel.AudioClips)
        {
            if (candidate.LinkId != linkId)
            {
                continue;
            }

            candidate.IsLeftTrimMarkerVisible = false;
            candidate.IsRightTrimMarkerVisible = false;
        }

        _activeTrimMarkerLinkId = null;
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

    private string? ResolveLaneLabelFromPointer(TimelineViewModel viewModel, PointerEventArgs eventArgs)
    {
        var timelineCanvas = this.FindControl<Grid>("TimelineCanvas");
        if (timelineCanvas is null || viewModel.VideoLanes.Count == 0)
        {
            return null;
        }

        var y = eventArgs.GetPosition(timelineCanvas).Y;
        y -= 62;
        if (y < 0)
        {
            return null;
        }

        var laneHeight = viewModel.LaneContainerHeight;
        var laneStep = laneHeight + 8;
        var laneIndex = (int)Math.Floor(y / laneStep);
        laneIndex = Math.Clamp(laneIndex, 0, viewModel.VideoLanes.Count - 1);

        return viewModel.VideoLanes[laneIndex].Label;
    }
}
