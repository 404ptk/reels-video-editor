using System;
using System.Collections.Generic;
using System.Linq;
using ReelsVideoEditor.App.Services.Export;
using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.Services.Composition;

public sealed class TimelineCompositionPlanner
{
    public TimelineCompositionPlan BuildPlan(
        IReadOnlyList<TimelineClipItem> videoClips,
        IReadOnlyList<VideoLaneItem> videoLanes)
    {
        var laneOrderByLabel = videoLanes
            .Select((lane, index) => new { lane.Label, Index = index })
            .ToDictionary(item => item.Label, item => item.Index, StringComparer.Ordinal);
        var hasSoloLanes = videoLanes.Any(lane => lane.IsSolo);
        var fallbackLaneIndex = ResolveFallbackLaneIndex(videoLanes);

        var visibleClips = new List<VisibleVideoClip>(videoClips.Count);
        foreach (var clip in videoClips)
        {
            if (clip.IsMediaMissing)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(clip.Path))
            {
                continue;
            }

            var lane = ResolveLaneForClip(videoLanes, clip.VideoLaneLabel);
            if (lane is null)
            {
                continue;
            }

            if (lane.IsHidden)
            {
                continue;
            }

            if (hasSoloLanes && !lane.IsSolo)
            {
                continue;
            }

            var laneIndex = laneOrderByLabel.TryGetValue(lane.Label, out var resolvedIndex)
                ? resolvedIndex
                : fallbackLaneIndex;
            visibleClips.Add(new VisibleVideoClip(clip, laneIndex));
        }

        return new TimelineCompositionPlan(visibleClips, laneOrderByLabel, fallbackLaneIndex);
    }

    public IReadOnlyList<CompositionActiveLayer> ResolveActiveVideoLayers(
        TimelineCompositionPlan plan,
        double timelineSeconds)
    {
        var active = plan.VisibleVideoClips
            .Where(item =>
                timelineSeconds >= item.Clip.StartSeconds
                && timelineSeconds <= item.Clip.StartSeconds + item.Clip.DurationSeconds)
            .OrderByDescending(item => item.LaneIndex)
            .ThenBy(item => item.Clip.StartSeconds)
            .ToList();

        if (active.Count == 0)
        {
            return [];
        }

        var result = new List<CompositionActiveLayer>(active.Count);
        for (var i = 0; i < active.Count; i++)
        {
            var item = active[i];
            var clipLocalSeconds = Math.Clamp(timelineSeconds - item.Clip.StartSeconds, 0, item.Clip.DurationSeconds);
            var localSeconds = Math.Clamp(
                item.Clip.SourceStartSeconds + clipLocalSeconds,
                item.Clip.SourceStartSeconds,
                item.Clip.SourceStartSeconds + item.Clip.DurationSeconds);
            var localMilliseconds = (long)Math.Round(localSeconds * 1000, MidpointRounding.AwayFromZero);

            result.Add(new CompositionActiveLayer(
                item.Clip,
                item.LaneIndex,
                localMilliseconds,
                DrawBlurredBackground: i == 0));
        }

        return result;
    }

    public TimelineClipItem? ResolvePreviewClip(
        TimelineCompositionPlan plan,
        double playheadSeconds)
    {
        var playheadMatch = plan.VisibleVideoClips
            .OrderBy(item => item.LaneIndex)
            .ThenByDescending(item => item.Clip.StartSeconds)
            .Select(item => item.Clip)
            .FirstOrDefault(clip =>
                playheadSeconds >= clip.StartSeconds
                && playheadSeconds <= clip.StartSeconds + clip.DurationSeconds);
        if (playheadMatch is not null)
        {
            return playheadMatch;
        }

        return plan.VisibleVideoClips
            .OrderBy(item => item.LaneIndex)
            .ThenBy(item => item.Clip.StartSeconds)
            .Select(item => item.Clip)
            .FirstOrDefault();
    }

    public PreviewAudioState ResolvePreviewAudioState(
        IReadOnlyList<TimelineClipItem> audioClips,
        bool isAudioMuted,
        double timelineSeconds)
    {
        if (isAudioMuted)
        {
            return PreviewAudioState.Silent;
        }

        var activeAudio = audioClips
            .Where(clip => !clip.IsMediaMissing)
            .OrderByDescending(clip => clip.StartSeconds)
            .FirstOrDefault(clip =>
                timelineSeconds >= clip.StartSeconds
                && timelineSeconds <= clip.StartSeconds + clip.DurationSeconds);
        if (activeAudio is null)
        {
            return PreviewAudioState.Silent;
        }

        var clipLocalSeconds = Math.Clamp(timelineSeconds - activeAudio.StartSeconds, 0, activeAudio.DurationSeconds);
        var localSeconds = Math.Clamp(
            activeAudio.SourceStartSeconds + clipLocalSeconds,
            activeAudio.SourceStartSeconds,
            activeAudio.SourceStartSeconds + activeAudio.DurationSeconds);
        var localMilliseconds = (long)Math.Round(localSeconds * 1000, MidpointRounding.AwayFromZero);
        var volume = Math.Clamp(activeAudio.VolumeLevel, 0.0, 1.0);
        var trackKey = activeAudio.LinkId == Guid.Empty
            ? activeAudio.Path
            : activeAudio.LinkId.ToString("N");

        return new PreviewAudioState(
            [new PreviewAudioTrackState(trackKey, activeAudio.Path, localMilliseconds, volume)],
            ShouldPlay: true);
    }

    public double ResolvePlaybackDurationSeconds(
        TimelineCompositionPlan plan,
        IReadOnlyList<TimelineClipItem> audioClips,
        bool isAudioMuted,
        double timelineDurationSeconds)
    {
        var maxVideoSeconds = plan.VisibleVideoClips
            .Select(item => item.Clip.StartSeconds + item.Clip.DurationSeconds)
            .DefaultIfEmpty(0)
            .Max();
        var maxAudioSeconds = isAudioMuted
            ? 0
            : audioClips
                .Where(clip => !clip.IsMediaMissing)
                .Select(clip => clip.StartSeconds + clip.DurationSeconds)
                .DefaultIfEmpty(0)
                .Max();

        var resolved = Math.Max(maxVideoSeconds, maxAudioSeconds);
        return Math.Clamp(resolved, 0.01, timelineDurationSeconds);
    }

    public IReadOnlyList<ExportVideoClipInput> BuildExportVideoInputs(TimelineCompositionPlan plan)
    {
        return plan.VisibleVideoClips
            .OrderByDescending(item => item.LaneIndex)
            .ThenBy(item => item.Clip.StartSeconds)
            .Select(item => new ExportVideoClipInput(
                item.Clip.Path,
                item.Clip.StartSeconds,
                item.Clip.DurationSeconds,
                item.Clip.SourceStartSeconds,
                item.LaneIndex,
                item.Clip.TransformX,
                item.Clip.TransformY,
                item.Clip.TransformScale,
                item.Clip.CropLeft,
                item.Clip.CropTop,
                item.Clip.CropRight,
                item.Clip.CropBottom))
            .ToList();
    }

    public IReadOnlyList<ExportAudioClipInput> BuildExportAudioInputs(IReadOnlyList<TimelineClipItem> audioClips)
    {
        return audioClips
            .Where(clip => !clip.IsMediaMissing)
            .OrderBy(clip => clip.StartSeconds)
            .Select(clip => new ExportAudioClipInput(
                clip.Path,
                clip.StartSeconds,
                clip.DurationSeconds,
                clip.SourceStartSeconds,
                clip.VolumeLevel))
            .ToList();
    }

    public int ResolveLaneLayerIndex(TimelineCompositionPlan plan, string laneLabel)
    {
        return plan.LaneOrderByLabel.TryGetValue(laneLabel, out var index)
            ? index
            : plan.FallbackLaneIndex;
    }

    private static int ResolveFallbackLaneIndex(IReadOnlyList<VideoLaneItem> videoLanes)
    {
        for (var i = 0; i < videoLanes.Count; i++)
        {
            if (videoLanes[i].IsPrimary)
            {
                return i;
            }
        }

        return videoLanes.Count > 0 ? 0 : int.MaxValue;
    }

    private static VideoLaneItem? ResolveLaneForClip(IReadOnlyList<VideoLaneItem> lanes, string laneLabel)
    {
        for (var i = 0; i < lanes.Count; i++)
        {
            if (string.Equals(lanes[i].Label, laneLabel, StringComparison.Ordinal))
            {
                return lanes[i];
            }
        }

        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i].IsPrimary)
            {
                return lanes[i];
            }
        }

        return lanes.FirstOrDefault();
    }
}

public sealed record TimelineCompositionPlan(
    IReadOnlyList<VisibleVideoClip> VisibleVideoClips,
    IReadOnlyDictionary<string, int> LaneOrderByLabel,
    int FallbackLaneIndex);

public readonly record struct VisibleVideoClip(TimelineClipItem Clip, int LaneIndex);

public readonly record struct CompositionActiveLayer(
    TimelineClipItem Clip,
    int LaneIndex,
    long PlaybackMilliseconds,
    bool DrawBlurredBackground);
