using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ReelsVideoEditor.App.ViewModels.Timeline;
using System;

namespace ReelsVideoEditor.App.Views.Timeline;

public partial class TimelinePanelView
{
    private ScrollViewer? _laneHeaderScrollViewer;
    private ScrollViewer? _timelineScrollViewer;
    private ScrollBar? _timelineVerticalScrollBar;
    private const double MouseWheelVerticalStep = 48;
    private bool _isSyncingVerticalScroll;

    private void InitializeScrollInfrastructure()
    {
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
    }

    private void TimelinePanelView_OnPointerWheelChangedTunnel(object? sender, PointerWheelEventArgs eventArgs)
    {
        if (DataContext is not TimelineViewModel viewModel)
        {
            return;
        }

        if (!eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control)
            && !eventArgs.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return;
        }

        HandleModifiedWheelGesture(viewModel, eventArgs, sender as ScrollViewer);
    }

    private void TimelineScrollViewer_OnPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
    {
        if (eventArgs.Handled)
        {
            return;
        }

        if (DataContext is TimelineViewModel viewModel)
        {
            if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Alt)
                || eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                HandleModifiedWheelGesture(viewModel, eventArgs, sender as ScrollViewer);
                return;
            }

            var currentOffsetY = _timelineScrollViewer?.Offset.Y ?? _laneHeaderScrollViewer?.Offset.Y ?? 0;
            var targetOffsetY = currentOffsetY - (eventArgs.Delta.Y * MouseWheelVerticalStep);
            SetVerticalOffset(targetOffsetY);
            eventArgs.Handled = true;
        }
    }

    private void HandleModifiedWheelGesture(TimelineViewModel viewModel, PointerWheelEventArgs eventArgs, ScrollViewer? senderScrollViewer)
    {
        if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            viewModel.ChangeLaneHeightFromWheel(eventArgs.Delta.Y);
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var viewportWidth = _timelineScrollViewer?.Bounds.Width ?? senderScrollViewer?.Bounds.Width ?? Bounds.Width;
            viewModel.ChangeZoomFromWheel(eventArgs.Delta.Y, viewportWidth);
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
}
