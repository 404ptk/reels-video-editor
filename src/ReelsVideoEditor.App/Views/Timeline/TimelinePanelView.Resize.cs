using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;
using System;
using System.Linq;

namespace ReelsVideoEditor.App.Views.Timeline;

public partial class TimelinePanelView
{
    private enum ClipResizeEdge
    {
        None,
        Left,
        Right
    }

    private TimelineClipItem? _resizingVideoClip;
    private Guid? _activeTrimMarkerLinkId;
    private bool _isResizingVideoClip;
    private ClipResizeEdge _activeResizeEdge;
    private double _resizingClipInitialStartSeconds;
    private double _resizingClipInitialDurationSeconds;
    private double _resizingClipInitialSourceStartSeconds;

    private const double ClipHorizontalResizeEdgeThreshold = 8;

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

    private static ClipResizeEdge ResolveClipResizeEdge(Control control, double localX)
    {
        var threshold = System.Math.Min(ClipHorizontalResizeEdgeThreshold, System.Math.Max(2, control.Bounds.Width / 3));
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

        if (_isAdjustingVideoFade)
        {
            control.Cursor = _activeFadeCorner switch
            {
                ClipFadeCorner.TopLeft => new Cursor(StandardCursorType.TopLeftCorner),
                ClipFadeCorner.TopRight => new Cursor(StandardCursorType.TopRightCorner),
                _ => null
            };
            return;
        }

        if (_isResizingVideoClip)
        {
            SetTrimMarker(viewModel, clip, _activeResizeEdge);
            control.Cursor = _activeResizeEdge is ClipResizeEdge.None ? null : new Cursor(StandardCursorType.SizeWestEast);
            return;
        }

        var localPosition = eventArgs.GetPosition(control);
        var fadeCorner = ResolveClipFadeCorner(control, localPosition);
        if (fadeCorner is not ClipFadeCorner.None)
        {
            control.Cursor = fadeCorner == ClipFadeCorner.TopLeft
                ? new Cursor(StandardCursorType.TopLeftCorner)
                : new Cursor(StandardCursorType.TopRightCorner);
            SetTrimMarker(viewModel, clip, ClipResizeEdge.None);
            return;
        }

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
}
