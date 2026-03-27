using System;
using System.Linq;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.ViewModels.Timeline;

public partial class TimelineViewModel
{
    public void SelectClipsInBox(double startX, double endX, double startY, double endY)
    {
        double minX = Math.Min(startX, endX);
        double maxX = Math.Max(startX, endX);
        double minY = Math.Min(startY, endY);
        double maxY = Math.Max(startY, endY);

        var changed = false;

        foreach (var clip in VideoClips)
        {
            var nextValue = IsClipIntersectingSelection(clip, minX, maxX, minY, maxY, isAudioClip: false);
            if (clip.IsSelected != nextValue)
            {
                clip.IsSelected = nextValue;
                changed = true;
            }
        }

        foreach (var clip in AudioClips)
        {
            var nextValue = IsClipIntersectingSelection(clip, minX, maxX, minY, maxY, isAudioClip: true);
            if (clip.IsSelected != nextValue)
            {
                clip.IsSelected = nextValue;
                changed = true;
            }
        }

        if (changed)
        {
            PreviewSelectionChanged?.Invoke();
        }
    }

    public void DeleteSelectedClips()
    {
        var videoToRemove = VideoClips.Where(c => c.IsSelected).ToList();
        var audioToRemoveNames = AudioClips.Where(c => c.IsSelected).Select(c => c.Name).ToList();

        var combinedToRemove = VideoClips
            .Where(c => c.IsSelected || audioToRemoveNames.Contains(c.Name))
            .ToList();

        if (combinedToRemove.Count == 0) return;

        var removedClips = combinedToRemove.ToList();

        undoStack.Push(() =>
        {
            foreach (var clip in removedClips)
            {
                clip.IsSelected = false;
                VideoClips.Add(clip);

                var linkedAudio = TimelineClipArrangementService.BuildLinkedAudioClip(clip);
                AudioClips.Add(linkedAudio);
                _ = LoadAudioWaveformAsync(linkedAudio);
            }
        });

        foreach (var clip in combinedToRemove)
        {
            VideoClips.Remove(clip);
        }
    }

    public void ClearSelection()
    {
        var changed = false;
        foreach (var clip in VideoClips)
        {
            if (clip.IsSelected)
            {
                clip.IsSelected = false;
                changed = true;
            }
        }

        foreach (var clip in AudioClips)
        {
            if (clip.IsSelected)
            {
                clip.IsSelected = false;
                changed = true;
            }
        }

        if (changed)
        {
            PreviewSelectionChanged?.Invoke();
        }
    }

    public void SelectSingleVideoClip(TimelineClipItem selectedClip)
    {
        var changed = false;
        foreach (var clip in VideoClips)
        {
            var nextValue = ReferenceEquals(clip, selectedClip);
            if (clip.IsSelected != nextValue)
            {
                clip.IsSelected = nextValue;
                changed = true;
            }
        }

        foreach (var clip in AudioClips)
        {
            var nextValue = clip.LinkId == selectedClip.LinkId;
            if (clip.IsSelected != nextValue)
            {
                clip.IsSelected = nextValue;
                changed = true;
            }
        }

        if (changed || string.IsNullOrWhiteSpace(selectedClip.Path))
        {
            PreviewSelectionChanged?.Invoke();
        }
    }

    private bool IsClipIntersectingSelection(
        TimelineClipItem clip,
        double minX,
        double maxX,
        double minY,
        double maxY,
        bool isAudioClip)
    {
        var clipLeft = clip.Left;
        var clipRight = clip.Left + clip.Width;
        var intersectsX = clipLeft < maxX && clipRight > minX;
        if (!intersectsX)
        {
            return false;
        }

        var laneTop = ResolveLaneTop(clip.VideoLaneLabel, isAudioClip);
        if (laneTop is null)
        {
            return false;
        }

        var clipTop = laneTop.Value + 6;
        var clipBottom = clipTop + ClipVisualHeight;
        return clipTop < maxY && clipBottom > minY;
    }

    private double? ResolveLaneTop(string laneLabel, bool isAudioClip)
    {
        var trackStart = TickSectionHeight + TrackTopSpacing;
        var laneStep = LaneContainerHeight + TrackGap;

        if (!isAudioClip)
        {
            var laneIndex = VideoLanes
                .Select((lane, index) => new { lane.Label, Index = index })
                .FirstOrDefault(item => string.Equals(item.Label, laneLabel, StringComparison.Ordinal))
                ?.Index;

            if (laneIndex is null)
            {
                return null;
            }

            return trackStart + (laneIndex.Value * laneStep);
        }

        var audioStart = trackStart
            + (VideoLaneCount * LaneContainerHeight)
            + (VideoLaneCount * TrackGap)
            + TrackGap;
        var audioLaneLabel = MapVideoLaneLabelToAudioLaneLabel(laneLabel);
        var audioLaneIndex = AudioLanes
            .Select((lane, index) => new { lane.Label, Index = index })
            .FirstOrDefault(item => string.Equals(item.Label, audioLaneLabel, StringComparison.Ordinal))
            ?.Index;

        if (audioLaneIndex is null)
        {
            return null;
        }

        return audioStart + (audioLaneIndex.Value * laneStep);
    }
}
