using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ReelsVideoEditor.App.DragDrop;
using ReelsVideoEditor.App.ViewModels.Timeline;
using System;

namespace ReelsVideoEditor.App.Views.Timeline;

public partial class TimelinePanelView : UserControl
{
    private Point? _dragStartPoint;

    public TimelinePanelView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, TimelinePanelView_OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, TimelineSeekSurface_OnPointerMoved, RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, TimelineSeekSurface_OnPointerReleased, RoutingStrategies.Bubble);
    }

    private void TimelinePanelView_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && DataContext is TimelineViewModel viewModel)
        {
            viewModel.DeleteSelectedClips();
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
