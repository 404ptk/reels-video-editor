using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.ViewModels.Timeline;

public partial class TimelineViewModel
{
    private const double ClipSnapThresholdPixels = 12;
    private const double TextClipDefaultDurationSeconds = 5;

    public void AddClipFromExplorer(string name, string path, double durationSeconds, double dropX, string? targetLaneLabel = null)
    {
        var clip = TimelineClipArrangementService.BuildClip(name, path, durationSeconds, dropX, TickWidth, TimelineDurationSeconds);
        var targetLane = ResolveLaneByLabel(targetLaneLabel) ?? ResolvePrimaryVideoLane();
        clip.VideoLaneLabel = targetLane?.Label ?? string.Empty;
        VideoClips.Add(clip);

        if (!IsStillImagePath(path))
        {
            var linkedAudio = TimelineClipArrangementService.BuildLinkedAudioClip(clip);
            AudioClips.Add(linkedAudio);
            _ = LoadAudioWaveformAsync(linkedAudio);
        }

        RebuildAudioLaneClipCollections();

        if (VideoClips.Count == 1)
        {
            PlayheadSeconds = clip.StartSeconds;
        }

        RefreshClipLevelLines();

        undoStack.Push(() =>
        {
            VideoClips.Remove(clip);
        });
    }

    public void AddTextPresetClip(string clipName, double dropX, string? targetLaneLabel = null)
    {
        var targetLane = ResolveLaneByLabel(targetLaneLabel) ?? ResolvePrimaryVideoLane();
        var clip = TimelineClipArrangementService.BuildClip(
            clipName,
            string.Empty,
            TextClipDefaultDurationSeconds,
            dropX,
            TickWidth,
            TimelineDurationSeconds);
        clip.VideoLaneLabel = targetLane?.Label ?? string.Empty;
        VideoClips.Add(clip);

        if (VideoClips.Count == 1)
        {
            PlayheadSeconds = clip.StartSeconds;
        }

        undoStack.Push(() =>
        {
            VideoClips.Remove(clip);
        });
    }

    public void MoveClipToStart(
        TimelineClipItem clip,
        double requestedStartSeconds,
        string? targetLaneLabel = null,
        bool enableSnapping = true)
    {
        var maxStartSeconds = Math.Max(0, TimelineDurationSeconds - clip.DurationSeconds);
        var clampedStartSeconds = Math.Clamp(requestedStartSeconds, 0, maxStartSeconds);
        var targetLane = ResolveLaneByLabel(targetLaneLabel);
        var laneChanged = targetLane is not null && !string.Equals(clip.VideoLaneLabel, targetLane.Label, StringComparison.Ordinal);
        var effectiveLaneLabel = laneChanged ? targetLane!.Label : clip.VideoLaneLabel;

        if (enableSnapping)
        {
            clampedStartSeconds = ResolveSnappedStartSeconds(clip, clampedStartSeconds, effectiveLaneLabel);
        }

        if (Math.Abs(clampedStartSeconds - clip.StartSeconds) < 0.0001)
        {
            if (!laneChanged)
            {
                return;
            }

            clip.VideoLaneLabel = targetLane!.Label;
            var laneOnlyLinkedAudio = AudioClips.FirstOrDefault(audio => audio.LinkId == clip.LinkId);
            if (laneOnlyLinkedAudio is not null)
            {
                laneOnlyLinkedAudio.VideoLaneLabel = targetLane.Label;
            }

            RebuildLaneClipCollections();
            RebuildAudioLaneClipCollections();
            NotifyPreviewClipIfChanged();
            return;
        }

        clip.StartSeconds = clampedStartSeconds;
        if (laneChanged)
        {
            clip.VideoLaneLabel = targetLane!.Label;
        }

        TimelineClipArrangementService.RebuildLayouts([clip], TickWidth);

        var linkedAudio = AudioClips.FirstOrDefault(audio => audio.LinkId == clip.LinkId);
        if (linkedAudio is not null)
        {
            linkedAudio.StartSeconds = clampedStartSeconds;
            if (laneChanged)
            {
                linkedAudio.VideoLaneLabel = targetLane!.Label;
            }

            TimelineClipArrangementService.RebuildLayouts([linkedAudio], TickWidth);
        }

        if (laneChanged)
        {
            RebuildLaneClipCollections();
            RebuildAudioLaneClipCollections();
        }

        NotifyPreviewClipIfChanged();
        UpdatePreviewLevels();
    }

    public void CommitClipMove(TimelineClipItem clip, double previousStartSeconds, string previousLaneLabel)
    {
        var currentStartSeconds = clip.StartSeconds;
        var laneChanged = !string.Equals(clip.VideoLaneLabel, previousLaneLabel, StringComparison.Ordinal);
        if (Math.Abs(currentStartSeconds - previousStartSeconds) < 0.0001 && !laneChanged)
        {
            return;
        }

        undoStack.Push(() =>
        {
            MoveClipToStart(clip, previousStartSeconds, previousLaneLabel, enableSnapping: false);
        });
    }

    private double ResolveSnappedStartSeconds(TimelineClipItem movingClip, double requestedStartSeconds, string laneLabel)
    {
        if (VideoClips.Count <= 1 || TickWidth <= 0.0001)
        {
            return requestedStartSeconds;
        }

        var thresholdSeconds = ClipSnapThresholdPixels / TickWidth;
        var maxStartSeconds = Math.Max(0, TimelineDurationSeconds - movingClip.DurationSeconds);

        var best = requestedStartSeconds;
        var bestDistance = double.MaxValue;

        void Consider(double candidateStart)
        {
            var clamped = Math.Clamp(candidateStart, 0, maxStartSeconds);
            var distance = Math.Abs(requestedStartSeconds - clamped);
            if (distance <= thresholdSeconds && distance < bestDistance)
            {
                bestDistance = distance;
                best = clamped;
            }
        }

        Consider(0);

        foreach (var other in VideoClips)
        {
            if (ReferenceEquals(other, movingClip))
            {
                continue;
            }

            if (!string.Equals(other.VideoLaneLabel, laneLabel, StringComparison.Ordinal))
            {
                continue;
            }

            var otherStart = other.StartSeconds;
            var otherEnd = other.StartSeconds + other.DurationSeconds;

            Consider(otherStart);
            Consider(otherEnd);
            Consider(otherStart - movingClip.DurationSeconds);
            Consider(otherEnd - movingClip.DurationSeconds);
        }

        return best;
    }

    public void SetAudioClipVolume(TimelineClipItem clip, double volumeLevel)
    {
        clip.VolumeLevel = Math.Clamp(volumeLevel, 0.0, 1.0);
        UpdateAudioClipLevelLine(clip);
        UpdatePreviewLevels();
    }

    public void Undo()
    {
        if (undoStack.Count > 0)
        {
            var action = undoStack.Pop();
            action();
        }
    }

    private void OnVideoClipsChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (isBatchUpdatingClips)
        {
            return;
        }

        RebuildLaneClipCollections();

        OnPropertyChanged(nameof(HasClips));
        PreviewClipChanged?.Invoke();

        if (VideoClips.Count == 0)
        {
            AudioClips.Clear();
            RebuildAudioLaneClipCollections();
            PlayheadSeconds = 0;
            RefreshClipLevelLines();
            UpdatePreviewLevels();
            return;
        }

        if (eventArgs.Action == NotifyCollectionChangedAction.Add && eventArgs.NewItems is { Count: > 0 })
        {
            RebuildAudioLaneClipCollections();
            RefreshClipLevelLines();
            UpdatePreviewLevels();
            return;
        }

        RebuildAudioFromVideo();

        PlayheadSeconds = ResolvePreviewClipStartSeconds();
        RefreshClipLevelLines();
        UpdatePreviewLevels();
    }

    private void RebuildAudioFromVideo()
    {
        var volumeByKey = AudioClips.ToDictionary(
            clip => BuildAudioClipKey(clip),
            clip => clip.VolumeLevel);

        AudioClips.Clear();

        foreach (var videoClip in VideoClips)
        {
            if (IsStillImagePath(videoClip.Path))
            {
                continue;
            }

            var audioClip = TimelineClipArrangementService.BuildLinkedAudioClip(videoClip);

            var key = BuildAudioClipKey(audioClip);
            if (volumeByKey.TryGetValue(key, out var volumeLevel))
            {
                audioClip.VolumeLevel = volumeLevel;
            }

            AudioClips.Add(audioClip);
            _ = LoadAudioWaveformAsync(audioClip);
        }

        RebuildAudioLaneClipCollections();
        RefreshClipLevelLines();
    }

    private async Task LoadAudioWaveformAsync(TimelineClipItem audioClip)
    {
        var waveform = await TimelineWaveformRenderService.TryRenderWaveformSegmentAsync(
            audioClip.Path,
            audioClip.SourceStartSeconds,
            audioClip.DurationSeconds,
            audioClip.SourceDurationSeconds);
        if (waveform is null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => audioClip.WaveformImage = waveform);
    }

    private double ResolvePreviewClipStartSeconds()
    {
        return ResolvePreviewClip()?.StartSeconds ?? 0;
    }

    private TimelineClipItem? ResolveSelectedVideoClip()
    {
        var selected = VideoClips.Where(clip => clip.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return null;
        }

        var activeSelected = selected
            .OrderBy(clip => ResolveLaneLayerIndex(clip.VideoLaneLabel))
            .ThenByDescending(clip => clip.StartSeconds)
            .FirstOrDefault(clip =>
                PlayheadSeconds >= clip.StartSeconds
                && PlayheadSeconds <= clip.StartSeconds + clip.DurationSeconds);
        if (activeSelected is not null)
        {
            return activeSelected;
        }

        return selected
            .OrderBy(clip => ResolveLaneLayerIndex(clip.VideoLaneLabel))
            .ThenBy(clip => clip.StartSeconds)
            .FirstOrDefault();
    }

    private void RefreshClipLevelLines()
    {
        foreach (var audioClip in AudioClips)
        {
            UpdateAudioClipLevelLine(audioClip);
        }
    }

    private void UpdateAudioClipLevelLine(TimelineClipItem clip)
    {
        var drawableHeight = Math.Max(2, ClipVisualHeight - 2);
        var volumeLevel = Math.Clamp(clip.VolumeLevel, 0.0, 1.0);

        clip.AudioLevelLineTop = (1.0 - volumeLevel) * drawableHeight;
        clip.IsAudioLevelLineVisible = clip.VolumeLevel < 0.999;

        var waveformHeight = Math.Max(1.0, drawableHeight * volumeLevel);
        clip.AudioWaveformVisualHeight = waveformHeight;
        clip.AudioWaveformVisualTop = (drawableHeight - waveformHeight) / 2.0;
    }
}
