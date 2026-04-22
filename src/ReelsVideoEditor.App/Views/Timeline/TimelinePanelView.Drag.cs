using Avalonia.Controls;
using Avalonia.Input;
using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;
using System;

namespace ReelsVideoEditor.App.Views.Timeline;

public partial class TimelinePanelView
{
    private TimelineClipItem? _draggingVideoClip;
    private bool _isDraggingVideoClip;
    private double _draggingClipInitialStartSeconds;
    private string _draggingClipInitialLaneLabel = string.Empty;
    private double _draggingClipPointerOffsetSeconds;

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
