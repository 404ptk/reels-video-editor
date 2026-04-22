using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;
using System;

namespace ReelsVideoEditor.App.Views.Timeline;

public partial class TimelinePanelView
{
    private enum ClipFadeCorner
    {
        None,
        TopLeft,
        TopRight
    }

    private TimelineClipItem? _fadingVideoClip;
    private bool _isAdjustingVideoFade;
    private ClipFadeCorner _activeFadeCorner;
    private double _fadeClipInitialInSeconds;
    private double _fadeClipInitialOutSeconds;

    private const double ClipCornerFadeHorizontalThreshold = 12;
    private const double ClipCornerFadeVerticalThreshold = 12;

    private void StartVideoClipFade(TimelineViewModel viewModel, TimelineClipItem clip, ClipFadeCorner fadeCorner)
    {
        _fadingVideoClip = clip;
        _activeFadeCorner = fadeCorner;
        _isAdjustingVideoFade = true;
        _dragStartPoint = null;

        _fadeClipInitialInSeconds = clip.FadeInDurationSeconds;
        _fadeClipInitialOutSeconds = clip.FadeOutDurationSeconds;

        viewModel.SelectSingleVideoClip(clip);
    }

    private void EndVideoClipFade(TimelineClipItem clip, TimelineViewModel viewModel, bool commit)
    {
        var previousFadeInSeconds = _fadeClipInitialInSeconds;
        var previousFadeOutSeconds = _fadeClipInitialOutSeconds;

        if (commit)
        {
            viewModel.CommitClipFade(clip, previousFadeInSeconds, previousFadeOutSeconds);
        }

        _isAdjustingVideoFade = false;
        _fadingVideoClip = null;
        _activeFadeCorner = ClipFadeCorner.None;
        _fadeClipInitialInSeconds = 0;
        _fadeClipInitialOutSeconds = 0;
    }

    private void UpdateVideoClipFadeFromPointer(TimelineViewModel viewModel, TimelineClipItem clip, double pointerSeconds)
    {
        var clipStart = clip.StartSeconds;
        var clipEnd = clip.StartSeconds + clip.DurationSeconds;

        if (_activeFadeCorner == ClipFadeCorner.TopLeft)
        {
            var requestedFadeIn = pointerSeconds - clipStart;
            viewModel.AdjustClipFadeFromLeftCorner(clip, requestedFadeIn);
            return;
        }

        if (_activeFadeCorner == ClipFadeCorner.TopRight)
        {
            var requestedFadeOut = clipEnd - pointerSeconds;
            viewModel.AdjustClipFadeFromRightCorner(clip, requestedFadeOut);
        }
    }

    private void UpdateVideoClipFadeFromLocalPosition(TimelineViewModel viewModel, TimelineClipItem clip, Control control, double localX)
    {
        var width = Math.Max(1, control.Bounds.Width);
        var normalizedX = Math.Clamp(localX, 0, width);
        var duration = Math.Max(0, clip.DurationSeconds);

        if (_activeFadeCorner == ClipFadeCorner.TopLeft)
        {
            var requestedFadeIn = (normalizedX / width) * duration;
            viewModel.AdjustClipFadeFromLeftCorner(clip, requestedFadeIn);
            return;
        }

        if (_activeFadeCorner == ClipFadeCorner.TopRight)
        {
            var requestedFadeOut = ((width - normalizedX) / width) * duration;
            viewModel.AdjustClipFadeFromRightCorner(clip, requestedFadeOut);
        }
    }

    private static ClipFadeCorner ResolveClipFadeCorner(Control control, Point localPosition)
    {
        var width = Math.Max(0, control.Bounds.Width);
        var height = Math.Max(0, control.Bounds.Height);
        if (width <= 0 || height <= 0)
        {
            return ClipFadeCorner.None;
        }

        var horizontalThreshold = Math.Min(ClipCornerFadeHorizontalThreshold, Math.Max(4, width / 3));
        var verticalThreshold = Math.Min(ClipCornerFadeVerticalThreshold, Math.Max(4, height / 2));
        if (localPosition.Y > verticalThreshold)
        {
            return ClipFadeCorner.None;
        }

        if (localPosition.X <= horizontalThreshold)
        {
            return ClipFadeCorner.TopLeft;
        }

        if (localPosition.X >= width - horizontalThreshold)
        {
            return ClipFadeCorner.TopRight;
        }

        return ClipFadeCorner.None;
    }
}
