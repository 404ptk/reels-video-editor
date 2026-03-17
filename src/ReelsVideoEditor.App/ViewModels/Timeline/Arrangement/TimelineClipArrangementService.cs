using System;
using System.Collections.Generic;

namespace ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

public sealed class TimelineClipArrangementService
{
    public TimelineClipItem BuildClip(string name, double durationSeconds, double dropX, double tickWidth, double timelineDurationSeconds)
    {
        var safeDuration = double.IsFinite(durationSeconds) && durationSeconds > 0 ? durationSeconds : 5;
        var startSeconds = Math.Max(0, dropX / tickWidth);

        if (startSeconds >= timelineDurationSeconds)
        {
            startSeconds = timelineDurationSeconds - 0.25;
        }

        var maxDuration = Math.Max(0.25, timelineDurationSeconds - startSeconds);
        var effectiveDuration = Math.Min(safeDuration, maxDuration);

        var clip = new TimelineClipItem(name, startSeconds, effectiveDuration);
        ApplyLayout(clip, tickWidth);
        return clip;
    }

    public void RebuildLayouts(IEnumerable<TimelineClipItem> clips, double tickWidth)
    {
        foreach (var clip in clips)
        {
            ApplyLayout(clip, tickWidth);
        }
    }

    private static void ApplyLayout(TimelineClipItem clip, double tickWidth)
    {
        clip.Left = clip.StartSeconds * tickWidth;
        clip.Width = Math.Max(24, clip.DurationSeconds * tickWidth);
    }
}
