using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Media;
using ReelsVideoEditor.App.Models;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.ViewModels.Timeline;

public partial class TimelineViewModel
{
    private const double ClipSnapThresholdPixels = 12;
    private const double TextClipDefaultDurationSeconds = 5;
    private const double MinClipDurationSeconds = 0.25;

    public void AddClipFromExplorer(string name, string path, double durationSeconds, double dropX, string? targetLaneLabel = null)
    {
        var clip = TimelineClipArrangementService.BuildClip(name, path, durationSeconds, dropX, TickWidth, TimelineDurationSeconds);
        var targetLane = ResolveLaneByLabel(targetLaneLabel) ?? ResolvePrimaryVideoLane();
        clip.VideoLaneLabel = targetLane?.Label ?? string.Empty;
        VideoClips.Add(clip);

        if (ShouldCreateLinkedAudio(path))
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

    public void AddTextPresetClip(TextPresetDefinition preset, double dropX, string? targetLaneLabel = null)
    {
        var targetLane = ResolveLaneByLabel(targetLaneLabel) ?? ResolvePrimaryVideoLane();
        var clip = TimelineClipArrangementService.BuildClip(
            preset.DisplayText,
            string.Empty,
            TextClipDefaultDurationSeconds,
            dropX,
            TickWidth,
            TimelineDurationSeconds);
        clip.VideoLaneLabel = targetLane?.Label ?? string.Empty;
        clip.TextContent = preset.DisplayText;
        clip.TextColorHex = preset.ColorHex;
        clip.TextFontFamily = preset.FontFamily;
        clip.TextFontSize = preset.FontSize;
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

    public void UpdateSelectedTextClipSettings(string text, string colorHex, double fontSize, string fontFamily)
    {
        var selectedTextClip = ResolveSelectedTextClip();
        if (selectedTextClip is null)
        {
            return;
        }

        var normalizedText = string.IsNullOrWhiteSpace(text) ? "Text" : text.Trim();
        var normalizedFontSize = Math.Clamp(fontSize, 10, 180);
        var normalizedFontFamily = string.IsNullOrWhiteSpace(fontFamily)
            ? selectedTextClip.TextFontFamily
            : fontFamily.Trim();
        var normalizedColorHex = selectedTextClip.TextColorHex;
        if (!string.IsNullOrWhiteSpace(colorHex) && Color.TryParse(colorHex.Trim(), out var parsedColor))
        {
            normalizedColorHex = parsedColor.ToString();
        }

        if (string.Equals(selectedTextClip.Name, normalizedText, StringComparison.Ordinal)
            && string.Equals(selectedTextClip.TextContent, normalizedText, StringComparison.Ordinal)
            && string.Equals(selectedTextClip.TextColorHex, normalizedColorHex, StringComparison.Ordinal)
            && Math.Abs(selectedTextClip.TextFontSize - normalizedFontSize) < 0.001
            && string.Equals(selectedTextClip.TextFontFamily, normalizedFontFamily, StringComparison.Ordinal))
        {
            return;
        }

        selectedTextClip.Name = normalizedText;
        selectedTextClip.TextContent = normalizedText;
        selectedTextClip.TextColorHex = normalizedColorHex;
        selectedTextClip.TextFontSize = normalizedFontSize;
        selectedTextClip.TextFontFamily = normalizedFontFamily;

        NotifyTextOverlayStateChanged();
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
            NotifyTextOverlayStateChanged();
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
        NotifyTextOverlayStateChanged();
    }

    public void ResizeClipFromLeft(TimelineClipItem clip, double requestedStartSeconds)
    {
        var clipEndSeconds = clip.StartSeconds + clip.DurationSeconds;
        var minStartBySource = IsSourceBoundedClip(clip)
            ? clip.StartSeconds - Math.Max(0, clip.SourceStartSeconds)
            : 0;
        var maxStartByDuration = clipEndSeconds - MinClipDurationSeconds;
        var clampedStartSeconds = Math.Clamp(requestedStartSeconds, minStartBySource, maxStartByDuration);
        clampedStartSeconds = Math.Max(0, clampedStartSeconds);

        var nextDurationSeconds = clipEndSeconds - clampedStartSeconds;
        var consumedFromSourceDelta = clip.StartSeconds - clampedStartSeconds;
        var nextSourceStartSeconds = IsSourceBoundedClip(clip)
            ? Math.Max(0, clip.SourceStartSeconds - consumedFromSourceDelta)
            : 0;

        ApplyClipResize(clip, clampedStartSeconds, nextDurationSeconds, nextSourceStartSeconds);
    }

    public void ResizeClipFromRight(TimelineClipItem clip, double requestedEndSeconds)
    {
        var minEndByDuration = clip.StartSeconds + MinClipDurationSeconds;
        var maxEndBySource = IsSourceBoundedClip(clip)
            ? clip.StartSeconds + Math.Max(MinClipDurationSeconds, clip.SourceDurationSeconds - clip.SourceStartSeconds)
            : TimelineDurationSeconds;
        var maxEndSeconds = Math.Min(TimelineDurationSeconds, maxEndBySource);
        var clampedEndSeconds = Math.Clamp(requestedEndSeconds, minEndByDuration, maxEndSeconds);

        var nextDurationSeconds = clampedEndSeconds - clip.StartSeconds;
        ApplyClipResize(clip, clip.StartSeconds, nextDurationSeconds, clip.SourceStartSeconds);
    }

    public void CommitClipResize(
        TimelineClipItem clip,
        double previousStartSeconds,
        double previousDurationSeconds,
        double previousSourceStartSeconds)
    {
        var changedStart = Math.Abs(clip.StartSeconds - previousStartSeconds) >= 0.0001;
        var changedDuration = Math.Abs(clip.DurationSeconds - previousDurationSeconds) >= 0.0001;
        var changedSourceStart = Math.Abs(clip.SourceStartSeconds - previousSourceStartSeconds) >= 0.0001;
        if (!changedStart && !changedDuration && !changedSourceStart)
        {
            return;
        }

        undoStack.Push(() =>
        {
            ApplyClipResize(clip, previousStartSeconds, previousDurationSeconds, previousSourceStartSeconds);
        });
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
            NotifyTextOverlayStateChanged();
            return;
        }

        if (eventArgs.Action == NotifyCollectionChangedAction.Add && eventArgs.NewItems is { Count: > 0 })
        {
            RebuildAudioLaneClipCollections();
            RefreshClipLevelLines();
            UpdatePreviewLevels();
            NotifyTextOverlayStateChanged();
            return;
        }

        RebuildAudioFromVideo();

        PlayheadSeconds = ResolvePreviewClipStartSeconds();
        RefreshClipLevelLines();
        UpdatePreviewLevels();
        NotifyTextOverlayStateChanged();
    }

    private void RebuildAudioFromVideo()
    {
        var volumeByKey = AudioClips.ToDictionary(
            clip => BuildAudioClipKey(clip),
            clip => clip.VolumeLevel);

        AudioClips.Clear();

        foreach (var videoClip in VideoClips)
        {
            if (!ShouldCreateLinkedAudio(videoClip.Path))
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

    private void ApplyClipResize(
        TimelineClipItem clip,
        double startSeconds,
        double durationSeconds,
        double sourceStartSeconds)
    {
        var boundedStartSeconds = Math.Clamp(startSeconds, 0, TimelineDurationSeconds);
        var boundedDurationSeconds = Math.Max(MinClipDurationSeconds, durationSeconds);
        var maxDurationOnTimeline = Math.Max(MinClipDurationSeconds, TimelineDurationSeconds - boundedStartSeconds);
        boundedDurationSeconds = Math.Min(boundedDurationSeconds, maxDurationOnTimeline);
        var boundedSourceStartSeconds = Math.Max(0, sourceStartSeconds);

        if (Math.Abs(clip.StartSeconds - boundedStartSeconds) < 0.0001
            && Math.Abs(clip.DurationSeconds - boundedDurationSeconds) < 0.0001
            && Math.Abs(clip.SourceStartSeconds - boundedSourceStartSeconds) < 0.0001)
        {
            return;
        }

        clip.StartSeconds = boundedStartSeconds;
        clip.DurationSeconds = boundedDurationSeconds;
        clip.SourceStartSeconds = boundedSourceStartSeconds;
        TimelineClipArrangementService.RebuildLayouts([clip], TickWidth);

        var linkedAudio = AudioClips.FirstOrDefault(audio => audio.LinkId == clip.LinkId);
        if (linkedAudio is not null)
        {
            linkedAudio.StartSeconds = boundedStartSeconds;
            linkedAudio.DurationSeconds = boundedDurationSeconds;
            linkedAudio.SourceStartSeconds = boundedSourceStartSeconds;
            TimelineClipArrangementService.RebuildLayouts([linkedAudio], TickWidth);
            UpdateAudioClipLevelLine(linkedAudio);
        }

        NotifyPreviewClipIfChanged();
        UpdatePreviewLevels();
        NotifyTextOverlayStateChanged();
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

    private static bool IsSourceBoundedClip(TimelineClipItem clip)
    {
        return !string.IsNullOrWhiteSpace(clip.Path);
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

        var sourceDuration = Math.Max(0.0001, clip.SourceDurationSeconds);
        var normalizedDuration = Math.Clamp(clip.DurationSeconds / sourceDuration, 0.0001, 1.0);
        var normalizedStart = Math.Clamp(clip.SourceStartSeconds / sourceDuration, 0.0, 1.0 - normalizedDuration);
        var clipVisualWidth = Math.Max(1.0, clip.Width);
        var waveformVisualWidth = clipVisualWidth / normalizedDuration;

        clip.AudioWaveformVisualWidth = waveformVisualWidth;
        clip.AudioWaveformVisualOffsetX = -(normalizedStart * waveformVisualWidth);
    }
}
