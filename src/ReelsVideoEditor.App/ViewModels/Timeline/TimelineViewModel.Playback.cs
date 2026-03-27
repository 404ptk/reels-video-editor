using System;
using System.Collections.Generic;
using System.Linq;
using ReelsVideoEditor.App.Services.Composition;
using ReelsVideoEditor.App.Services.Export;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.ViewModels.Timeline;

public partial class TimelineViewModel
{
    partial void OnPlayheadSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(PlayheadLeft));
        OnPropertyChanged(nameof(PlayheadVisualLeft));
        NotifyPreviewClipIfChanged();
        UpdatePreviewLevels();
        NotifyTextOverlayStateChanged();
    }

    partial void OnIsPlaybackActiveChanged(bool value)
    {
    }

    public string? ResolvePreviewClipPath()
    {
        var previewClip = ResolvePreviewClip();
        if (previewClip is null || string.IsNullOrWhiteSpace(previewClip.Path))
        {
            return null;
        }

        return previewClip.Path;
    }

    public IReadOnlyList<PreviewVideoLayer> ResolvePreviewVideoLayers(long playbackMilliseconds)
    {
        var timelineSeconds = ResolveTimelineSecondsForLayerPlayback(playbackMilliseconds);
        var hasAnySelectedVideoClip = VideoClips.Any(clip => clip.IsSelected);
        var plan = BuildCompositionPlan();
        var activeLayers = compositionPlanner.ResolveActiveVideoLayers(plan, timelineSeconds);
        if (activeLayers.Count == 0)
        {
            return [];
        }

        var result = new List<PreviewVideoLayer>(activeLayers.Count);
        for (var i = 0; i < activeLayers.Count; i++)
        {
            var layer = activeLayers[i];
            var clip = layer.Clip;
            if (string.IsNullOrWhiteSpace(clip.Path))
            {
                continue;
            }

            result.Add(new PreviewVideoLayer(
                clip.Path,
                layer.PlaybackMilliseconds,
                layer.DrawBlurredBackground,
                IsSelected: clip.IsSelected,
                HasAnySelectedVideoClip: hasAnySelectedVideoClip,
                TransformX: clip.TransformX,
                TransformY: clip.TransformY,
                TransformScale: clip.TransformScale,
                CropLeft: clip.CropLeft,
                CropTop: clip.CropTop,
                CropRight: clip.CropRight,
                CropBottom: clip.CropBottom));
        }

        return result;
    }

    public PreviewAudioState ResolvePreviewAudioState(long playbackMilliseconds)
    {
        if (IsAudioMuted)
        {
            return PreviewAudioState.Silent;
        }

        var timelineSeconds = ResolveTimelineSecondsForLayerPlayback(playbackMilliseconds);
        var activeAudio = ResolveActiveAudioClipsAt(timelineSeconds);
        if (activeAudio.Count == 0)
        {
            return PreviewAudioState.Silent;
        }

        var tracks = activeAudio
            .Where(clip => !string.IsNullOrWhiteSpace(clip.Path))
            .Select(clip =>
            {
                var clipLocalSeconds = Math.Clamp(timelineSeconds - clip.StartSeconds, 0, clip.DurationSeconds);
                var localSeconds = Math.Clamp(
                    clip.SourceStartSeconds + clipLocalSeconds,
                    clip.SourceStartSeconds,
                    clip.SourceStartSeconds + clip.DurationSeconds);
                var localMilliseconds = (long)Math.Round(localSeconds * 1000, MidpointRounding.AwayFromZero);
                var volume = Math.Clamp(clip.VolumeLevel, 0.0, 1.0);
                var trackKey = clip.LinkId == Guid.Empty
                    ? BuildAudioClipKey(clip)
                    : clip.LinkId.ToString("N");
                return new PreviewAudioTrackState(trackKey, clip.Path, localMilliseconds, volume);
            })
            .ToList();

        return tracks.Count == 0
            ? PreviewAudioState.Silent
            : new PreviewAudioState(tracks, ShouldPlay: true);
    }

    public IReadOnlyList<ExportAudioClipInput> ResolveExportAudioInputs()
    {
        return compositionPlanner.BuildExportAudioInputs(ResolveActiveAudioClips());
    }

    public long ResolvePlaybackDurationMilliseconds()
    {
        var durationSeconds = ResolvePlaybackDurationSeconds();
        return (long)Math.Round(durationSeconds * 1000, MidpointRounding.AwayFromZero);
    }

    public bool ShouldPreviewClipUseBlurredBackground()
    {
        var previewClip = ResolvePreviewClip();
        if (previewClip is null)
        {
            return true;
        }

        var lane = ResolveLaneByLabel(previewClip.VideoLaneLabel) ?? ResolvePrimaryVideoLane();
        if (lane is null)
        {
            return true;
        }

        // Foreground overlays on upper lanes should not generate their own blurred fill.
        return lane.IsPrimary;
    }

    public bool HasSelectedVideoClip()
    {
        return VideoClips.Any(clip => clip.IsSelected);
    }

    public PreviewClipTransform ResolveTransformTargetState()
    {
        var targetClip = ResolveTransformTargetClip();
        if (targetClip is null)
        {
            return PreviewClipTransform.Default;
        }

        return new PreviewClipTransform(
            targetClip.TransformX,
            targetClip.TransformY,
            targetClip.TransformScale,
            targetClip.CropLeft,
            targetClip.CropTop,
            targetClip.CropRight,
            targetClip.CropBottom);
    }

    public void ApplyTransformToTarget(
        double transformX,
        double transformY,
        double transformScale,
        double cropLeft,
        double cropTop,
        double cropRight,
        double cropBottom)
    {
        var targetClip = ResolveTransformTargetClip();
        if (targetClip is null)
        {
            return;
        }

        targetClip.TransformX = transformX;
        targetClip.TransformY = transformY;
        targetClip.TransformScale = Math.Max(0.1, transformScale);
        targetClip.CropLeft = Math.Clamp(cropLeft, 0.0, 0.95);
        targetClip.CropTop = Math.Clamp(cropTop, 0.0, 0.95);
        targetClip.CropRight = Math.Clamp(cropRight, 0.0, 0.95);
        targetClip.CropBottom = Math.Clamp(cropBottom, 0.0, 0.95);
    }

    public void UpdatePlayheadFromPlayback(long playbackMilliseconds)
    {
        if (!IsPlaybackActive)
        {
            return;
        }

        var safePlaybackMilliseconds = Math.Max(0, playbackMilliseconds);

        if (lastPlaybackMilliseconds >= 0)
        {
            safePlaybackMilliseconds = Math.Max(safePlaybackMilliseconds, lastPlaybackMilliseconds);
        }

        lastPlaybackMilliseconds = safePlaybackMilliseconds;

        var playbackSeconds = safePlaybackMilliseconds / 1000.0;
        var clampedSeconds = Math.Clamp(playbackSeconds, 0, playbackMaxSeconds);
        PlayheadSeconds = clampedSeconds;
    }

    public void SeekToPosition(double pointerX)
    {
        var playheadSeconds = Math.Max(0, (pointerX - 10) / TickWidth);
        var clampedSeconds = Math.Clamp(playheadSeconds, 0, TimelineDurationSeconds);

        PlayheadSeconds = clampedSeconds;
        lastPlaybackMilliseconds = -1;
        PlaybackSeekRequested?.Invoke(ResolvePlaybackSeekMilliseconds(clampedSeconds));
    }

    public void SetPlaybackActive(bool isPlaying)
    {
        IsPlaybackActive = isPlaying;
        lastPlaybackMilliseconds = -1;

        if (!isPlaying)
        {
            return;
        }

        playbackMaxSeconds = ResolvePlaybackDurationSeconds();
    }

    public void RefreshPreviewLevels()
    {
        UpdatePreviewLevels();
    }

    public void RefreshTextOverlayState()
    {
        NotifyTextOverlayStateChanged();
    }

    private TimelineClipItem? ResolvePreviewClip()
    {
        if (VideoClips.Count == 0)
        {
            return null;
        }

        var plan = BuildCompositionPlan();
        return compositionPlanner.ResolvePreviewClip(plan, PlayheadSeconds);
    }

    private TimelineClipItem? ResolveTransformTargetClip()
    {
        var selectedClip = ResolveSelectedVideoClip();
        if (selectedClip is not null)
        {
            return selectedClip;
        }

        return ResolvePreviewClip();
    }

    private IEnumerable<TimelineClipItem> ResolveVisibleVideoClips()
    {
        var plan = BuildCompositionPlan();
        foreach (var item in plan.VisibleVideoClips)
        {
            yield return item.Clip;
        }
    }

    private int ResolveLaneLayerIndex(string laneLabel)
    {
        return compositionPlanner.ResolveLaneLayerIndex(BuildCompositionPlan(), laneLabel);
    }

    private long ResolvePlaybackSeekMilliseconds(double timelineSeconds)
    {
        return (long)Math.Round(Math.Clamp(timelineSeconds, 0, TimelineDurationSeconds) * 1000, MidpointRounding.AwayFromZero);
    }

    private double ResolveTimelineSecondsForLayerPlayback(long playbackMilliseconds)
    {
        return Math.Clamp(playbackMilliseconds / 1000.0, 0, TimelineDurationSeconds);
    }

    private double ResolvePlaybackDurationSeconds()
    {
        var plan = BuildCompositionPlan();
        var baseDurationSeconds = compositionPlanner.ResolvePlaybackDurationSeconds(
            plan,
            ResolveActiveAudioClips(),
            IsAudioMuted,
            TimelineDurationSeconds);

        var textOnlyDurationSeconds = ResolveVisibleTextOnlyPlaybackEndSeconds();
        return Math.Clamp(Math.Max(baseDurationSeconds, textOnlyDurationSeconds), 0.01, TimelineDurationSeconds);
    }

    private double ResolveVisibleTextOnlyPlaybackEndSeconds()
    {
        if (VideoClips.Count == 0)
        {
            return 0;
        }

        var hasSoloLanes = VideoLanes.Any(lane => lane.IsSolo);

        return VideoClips
            .Where(clip => string.IsNullOrWhiteSpace(clip.Path))
            .Where(clip =>
            {
                var lane = ResolveLaneByLabel(clip.VideoLaneLabel) ?? ResolvePrimaryVideoLane();
                if (lane is null)
                {
                    return false;
                }

                if (lane.IsHidden)
                {
                    return false;
                }

                if (hasSoloLanes && !lane.IsSolo)
                {
                    return false;
                }

                return true;
            })
            .Select(clip => clip.StartSeconds + clip.DurationSeconds)
            .DefaultIfEmpty(0)
            .Max();
    }

    private TimelineCompositionPlan BuildCompositionPlan()
    {
        return compositionPlanner.BuildPlan(VideoClips, VideoLanes);
    }

    private void NotifyPreviewClipIfChanged()
    {
        var previewClip = ResolvePreviewClip();
        if (ReferenceEquals(previewClip, lastPreviewClip))
        {
            return;
        }

        lastPreviewClip = previewClip;
        PreviewClipChanged?.Invoke();
    }

    private void UpdatePreviewLevels()
    {
        var audioVolume = ResolveAudioVolumeAt(PlayheadSeconds);
        PreviewLevelsChanged?.Invoke(audioVolume);
    }

    private void NotifyTextOverlayStateChanged()
    {
        TextOverlayStateChanged?.Invoke(ResolveTextOverlayState());
    }

    private TimelineTextOverlayState ResolveTextOverlayState()
    {
        var hasSoloLanes = VideoLanes.Any(lane => lane.IsSolo);
        var activeTextClip = VideoClips
            .Where(clip => IsTextTimelineClip(clip)
                && PlayheadSeconds >= clip.StartSeconds
                && PlayheadSeconds <= clip.StartSeconds + clip.DurationSeconds)
            .Where(clip =>
            {
                var lane = ResolveLaneByLabel(clip.VideoLaneLabel) ?? ResolvePrimaryVideoLane();
                if (lane is null)
                {
                    return false;
                }

                if (lane.IsHidden)
                {
                    return false;
                }

                if (hasSoloLanes && !lane.IsSolo)
                {
                    return false;
                }

                return true;
            })
            .OrderBy(clip => ResolveLaneLayerIndex(clip.VideoLaneLabel))
            .ThenByDescending(clip => clip.StartSeconds)
            .FirstOrDefault();

        if (activeTextClip is null)
        {
            return new TimelineTextOverlayState(false, string.Empty);
        }

        return new TimelineTextOverlayState(true, activeTextClip.Name);
    }

    private static bool IsTextTimelineClip(TimelineClipItem clip)
    {
        return string.IsNullOrWhiteSpace(clip.Path);
    }

    private double ResolveAudioVolumeAt(double seconds)
    {
        var activeClips = ResolveActiveAudioClipsAt(seconds);
        if (activeClips.Count > 0)
        {
            var mixedVolume = activeClips.Sum(clip => Math.Clamp(clip.VolumeLevel, 0.0, 1.0));
            return Math.Clamp(mixedVolume, 0.0, 1.0);
        }

        var previewClip = ResolvePreviewClip();
        if (previewClip is null)
        {
            return 1.0;
        }

        var linkedAudio = ResolveActiveAudioClips().FirstOrDefault(clip =>
            string.Equals(clip.Path, previewClip.Path, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(clip.StartSeconds - previewClip.StartSeconds) < 0.001);

        return linkedAudio?.VolumeLevel ?? 1.0;
    }
}
