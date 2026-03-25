using System;
using System.Collections.Generic;
using System.Linq;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.ViewModels.Timeline;

public partial class TimelineViewModel
{
    private VideoLaneItem? ResolvePrimaryVideoLane()
    {
        return VideoLanes.FirstOrDefault(lane => lane.IsPrimary) ?? VideoLanes.FirstOrDefault();
    }

    private VideoLaneItem? ResolveLaneByLabel(string? laneLabel)
    {
        if (string.IsNullOrWhiteSpace(laneLabel))
        {
            return null;
        }

        return VideoLanes.FirstOrDefault(lane => string.Equals(lane.Label, laneLabel, StringComparison.Ordinal));
    }

    private AudioLaneItem? ResolveAudioLaneByVideoLabel(string? videoLaneLabel)
    {
        var audioLaneLabel = MapVideoLaneLabelToAudioLaneLabel(videoLaneLabel);
        if (string.IsNullOrWhiteSpace(audioLaneLabel))
        {
            return AudioLanes.FirstOrDefault();
        }

        return AudioLanes.FirstOrDefault(lane => string.Equals(lane.Label, audioLaneLabel, StringComparison.Ordinal))
            ?? AudioLanes.FirstOrDefault();
    }

    private void RebuildAudioLaneCollections()
    {
        var laneStateByLabel = AudioLanes.ToDictionary(
            lane => lane.Label,
            lane => (lane.IsSolo, lane.IsMuted),
            StringComparer.Ordinal);

        AudioLanes.Clear();

        var projectedAudioLanes = VideoLanes
            .Select(lane => new { Label = MapVideoLaneLabelToAudioLaneLabel(lane.Label), lane.IsPrimary })
            .DistinctBy(x => x.Label)
            .ToList();

        var primaryAudioLane = projectedAudioLanes.FirstOrDefault(lane => lane.IsPrimary);
        if (primaryAudioLane is not null)
        {
            var primaryState = laneStateByLabel.TryGetValue(primaryAudioLane.Label, out var existingPrimaryState)
                ? existingPrimaryState
                : (false, false);
            AudioLanes.Add(new AudioLaneItem(primaryAudioLane.Label, true, primaryState.Item1, primaryState.Item2));
        }

        foreach (var projectedLane in projectedAudioLanes
                     .Where(lane => !lane.IsPrimary)
                     .OrderBy(lane => ResolveAudioLaneOrdinal(lane.Label))
                     .ThenBy(lane => lane.Label, StringComparer.Ordinal))
        {
            var state = laneStateByLabel.TryGetValue(projectedLane.Label, out var existingState)
                ? existingState
                : (false, false);
            AudioLanes.Add(new AudioLaneItem(projectedLane.Label, false, state.Item1, state.Item2));
        }

        if (AudioLanes.Count == 0)
        {
            AudioLanes.Add(new AudioLaneItem("AUDIO", true, false, false));
        }

        OnPropertyChanged(nameof(AudioLaneCount));
        OnPropertyChanged(nameof(TimelineCanvasHeight));
        OnPropertyChanged(nameof(PlayheadHeight));
    }

    private void RebuildAudioLaneClipCollections()
    {
        foreach (var lane in AudioLanes)
        {
            lane.Clips.Clear();
        }

        foreach (var clip in AudioClips)
        {
            var lane = ResolveAudioLaneByVideoLabel(clip.VideoLaneLabel);
            lane?.Clips.Add(clip);
        }
    }

    private static string MapVideoLaneLabelToAudioLaneLabel(string? videoLaneLabel)
    {
        if (string.IsNullOrWhiteSpace(videoLaneLabel))
        {
            return "AUDIO";
        }

        return videoLaneLabel.StartsWith("VIDEO", StringComparison.Ordinal)
            ? $"AUDIO{videoLaneLabel[5..]}"
            : $"AUDIO {videoLaneLabel}";
    }

    private static string BuildAudioClipKey(TimelineClipItem clip)
    {
        return $"{clip.Path}|{clip.StartSeconds:F3}|{clip.DurationSeconds:F3}|{clip.Name}|{clip.VideoLaneLabel}";
    }

    private static bool IsStillImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = System.IO.Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<TimelineClipItem> ResolveActiveAudioClips()
    {
        if (AudioClips.Count == 0)
        {
            return [];
        }

        var hasSoloLane = AudioLanes.Any(lane => lane.IsSolo);

        return AudioClips
            .Where(clip =>
            {
                var lane = ResolveAudioLaneByVideoLabel(clip.VideoLaneLabel);
                if (lane is null)
                {
                    return false;
                }

                if (lane.IsMuted)
                {
                    return false;
                }

                if (hasSoloLane && !lane.IsSolo)
                {
                    return false;
                }

                return true;
            })
            .ToList();
    }

    private TimelineClipItem? ResolveActiveAudioClipAt(double seconds)
    {
        if (AudioClips.Count == 0)
        {
            return null;
        }

        var hasSoloLane = AudioLanes.Any(lane => lane.IsSolo);
        TimelineClipItem? activeClip = null;

        foreach (var clip in AudioClips)
        {
            var lane = ResolveAudioLaneByVideoLabel(clip.VideoLaneLabel);
            if (lane is null)
            {
                continue;
            }

            if (lane.IsMuted)
            {
                continue;
            }

            if (hasSoloLane && !lane.IsSolo)
            {
                continue;
            }

            if (seconds < clip.StartSeconds || seconds > clip.StartSeconds + clip.DurationSeconds)
            {
                continue;
            }

            if (activeClip is null || clip.StartSeconds > activeClip.StartSeconds)
            {
                activeClip = clip;
            }
        }

        return activeClip;
    }

    private static int ResolveAudioLaneOrdinal(string laneLabel)
    {
        if (string.Equals(laneLabel, "AUDIO", StringComparison.Ordinal))
        {
            return 1;
        }

        var suffix = laneLabel.Replace("AUDIO", string.Empty, StringComparison.Ordinal).Trim();
        return int.TryParse(suffix, out var parsed) && parsed > 0 ? parsed : int.MaxValue;
    }

    private void RebuildLaneClipCollections()
    {
        foreach (var lane in VideoLanes)
        {
            lane.Clips.Clear();
        }

        var fallbackLane = ResolvePrimaryVideoLane();
        if (fallbackLane is null)
        {
            return;
        }

        foreach (var clip in VideoClips)
        {
            var lane = ResolveLaneByLabel(clip.VideoLaneLabel) ?? fallbackLane;
            if (!string.Equals(clip.VideoLaneLabel, lane.Label, StringComparison.Ordinal))
            {
                clip.VideoLaneLabel = lane.Label;
            }

            lane.Clips.Add(clip);
        }
    }
}
