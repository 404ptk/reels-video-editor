using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.ViewModels.Timeline;

public partial class TimelineViewModel
{
    private TimelineClipCopyPayload? copiedClipPayload;
    private double? nextPasteStartSeconds;

    private sealed record TimelineClipCopyPayload(
        string Name,
        string Path,
        double DurationSeconds,
        double SourceStartSeconds,
        double SourceDurationSeconds,
        double VolumeLevel,
        string VideoLaneLabel,
        double TransformX,
        double TransformY,
        double TransformScale,
        double CropLeft,
        double CropTop,
        double CropRight,
        double CropBottom,
        string TextContent,
        string TextColorHex,
        string TextOutlineColorHex,
        double TextOutlineThickness,
        double TextFontSize,
        string TextFontFamily,
        double TextLineHeightMultiplier,
        double TextLetterSpacing,
        string TextRevealEffect);

    public void CopySelectedClip()
    {
        var source = ResolveSelectedVideoClip();
        if (source is null)
        {
            var selectedAudio = AudioClips.FirstOrDefault(clip => clip.IsSelected);
            source = selectedAudio is null
                ? null
                : VideoClips.FirstOrDefault(clip => clip.LinkId == selectedAudio.LinkId);
        }

        if (source is null)
        {
            return;
        }

        copiedClipPayload = new TimelineClipCopyPayload(
            source.Name,
            source.Path,
            source.DurationSeconds,
            source.SourceStartSeconds,
            source.SourceDurationSeconds,
            source.VolumeLevel,
            source.VideoLaneLabel,
            source.TransformX,
            source.TransformY,
            source.TransformScale,
            source.CropLeft,
            source.CropTop,
            source.CropRight,
            source.CropBottom,
            source.TextContent,
            source.TextColorHex,
            source.TextOutlineColorHex,
            source.TextOutlineThickness,
            source.TextFontSize,
            source.TextFontFamily,
            source.TextLineHeightMultiplier,
            source.TextLetterSpacing,
            source.TextRevealEffect);
            nextPasteStartSeconds = null;
    }

    public bool PasteCopiedClipAtPlayhead()
    {
        if (copiedClipPayload is null)
        {
            return false;
        }

        var playheadSeconds = Math.Clamp(PlayheadSeconds, 0, TimelineDurationSeconds);
        var useSequentialPaste = nextPasteStartSeconds.HasValue
            && Math.Abs(playheadSeconds - nextPasteStartSeconds.Value) < 0.0001;
        var startSeconds = useSequentialPaste
            ? nextPasteStartSeconds.GetValueOrDefault(playheadSeconds)
            : playheadSeconds;

        var maxDurationOnTimeline = TimelineDurationSeconds - startSeconds;
        if (maxDurationOnTimeline < MinClipDurationSeconds)
        {
            nextPasteStartSeconds = null;
            return false;
        }

        var durationSeconds = Math.Clamp(
            copiedClipPayload.DurationSeconds,
            MinClipDurationSeconds,
            maxDurationOnTimeline);

        var pastedClip = new TimelineClipItem(
            copiedClipPayload.Name,
            copiedClipPayload.Path,
            startSeconds,
            durationSeconds,
            null,
            copiedClipPayload.SourceStartSeconds,
            copiedClipPayload.SourceDurationSeconds)
        {
            VideoLaneLabel = copiedClipPayload.VideoLaneLabel,
            TransformX = copiedClipPayload.TransformX,
            TransformY = copiedClipPayload.TransformY,
            TransformScale = copiedClipPayload.TransformScale,
            CropLeft = copiedClipPayload.CropLeft,
            CropTop = copiedClipPayload.CropTop,
            CropRight = copiedClipPayload.CropRight,
            CropBottom = copiedClipPayload.CropBottom,
            TextContent = copiedClipPayload.TextContent,
            TextColorHex = copiedClipPayload.TextColorHex,
            TextOutlineColorHex = copiedClipPayload.TextOutlineColorHex,
            TextOutlineThickness = copiedClipPayload.TextOutlineThickness,
            TextFontSize = copiedClipPayload.TextFontSize,
            TextFontFamily = copiedClipPayload.TextFontFamily,
            TextLineHeightMultiplier = copiedClipPayload.TextLineHeightMultiplier,
            TextLetterSpacing = copiedClipPayload.TextLetterSpacing,
            TextRevealEffect = copiedClipPayload.TextRevealEffect
        };

        TimelineClipArrangementService.RebuildLayouts([pastedClip], TickWidth);
        VideoClips.Add(pastedClip);

        TimelineClipItem? pastedAudio = null;
        if (ShouldCreateLinkedAudio(copiedClipPayload.Path))
        {
            pastedAudio = TimelineClipArrangementService.BuildLinkedAudioClip(pastedClip);
            pastedAudio.VolumeLevel = copiedClipPayload.VolumeLevel;
            AudioClips.Add(pastedAudio);
            UpdateAudioClipLevelLine(pastedAudio);
            RebuildAudioLaneClipCollections();
            _ = LoadAudioWaveformAsync(pastedAudio);
        }

        ClearSelection();
        SelectSingleVideoClip(pastedClip);
        var nextStartSeconds = Math.Clamp(startSeconds + durationSeconds, 0, TimelineDurationSeconds);
        PlayheadSeconds = nextStartSeconds;
        nextPasteStartSeconds = nextStartSeconds;

        var undoVideo = pastedClip;
        var undoAudio = pastedAudio;
        undoStack.Push(() =>
        {
            if (undoAudio is not null)
            {
                AudioClips.Remove(undoAudio);
            }

            VideoClips.Remove(undoVideo);
            nextPasteStartSeconds = null;
        });

        return true;
    }

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

    [RelayCommand]
    public void DeleteSelectedClips()
    {
        var selectedVideo = VideoClips.Where(c => c.IsSelected).ToList();
        var selectedAudio = AudioClips.Where(c => c.IsSelected).ToList();

        // Find video clips linked to selected audio clips
        var videoToRemoveByAudio = selectedAudio
            .SelectMany(a => VideoClips.Where(v => v.LinkId == a.LinkId))
            .ToList();

        var videoToRemove = selectedVideo
            .Concat(videoToRemoveByAudio)
            .Distinct()
            .ToList();

        // Find audio clips linked to selected video clips
        var audioToRemoveByVideo = selectedVideo
            .SelectMany(v => AudioClips.Where(a => a.LinkId == v.LinkId))
            .ToList();

        var audioToRemove = selectedAudio
            .Concat(audioToRemoveByVideo)
            .Distinct()
            .ToList();

        if (videoToRemove.Count == 0 && audioToRemove.Count == 0) return;

        var removedVideoClips = videoToRemove.ToList();
        var removedAudioClips = audioToRemove.ToList();

        undoStack.Push(() =>
        {
            foreach (var clip in removedVideoClips)
            {
                clip.IsSelected = false;
                VideoClips.Add(clip);

                if (ShouldCreateLinkedAudio(clip.Path))
                {
                    var linkedAudio = TimelineClipArrangementService.BuildLinkedAudioClip(clip);
                    AudioClips.Add(linkedAudio);
                    _ = LoadAudioWaveformAsync(linkedAudio);
                }
            }

            foreach (var clip in removedAudioClips)
            {
                clip.IsSelected = false;
                AudioClips.Add(clip);
            }
        });

        foreach (var clip in videoToRemove)
        {
            VideoClips.Remove(clip);
        }

        foreach (var clip in audioToRemove)
        {
            AudioClips.Remove(clip);
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

    public void SelectClipsInLane(string laneLabel)
    {
        var changed = false;

        foreach (var clip in VideoClips)
        {
            var nextValue = string.Equals(clip.VideoLaneLabel, laneLabel, StringComparison.Ordinal);
            if (clip.IsSelected != nextValue)
            {
                clip.IsSelected = nextValue;
                changed = true;
            }
        }

        foreach (var clip in AudioClips)
        {
            var nextValue = string.Equals(clip.VideoLaneLabel, laneLabel, StringComparison.Ordinal);
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
