using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReelsVideoEditor.App.ViewModels.Timeline;

public partial class TimelineViewModel
{
    private const double MinClipFragmentSeconds = 0.05;
    private const double CutterSnapThresholdPixels = 10.0;
    private const double CutterMarkerContainerHalfWidth = 7.0;

    [ObservableProperty]
    private TimelineTool activeTool = TimelineTool.Mouse;

    [ObservableProperty]
    private double cutterMarkerSeconds;

    [ObservableProperty]
    private bool isCutterMarkerVisible;

    public bool IsMouseToolActive => ActiveTool == TimelineTool.Mouse;
    public bool IsCutterToolActive => ActiveTool == TimelineTool.Cutter;
    public double CutterMarkerLeft => Math.Clamp(CutterMarkerSeconds, 0, TimelineDurationSeconds) * TickWidth;
    public double CutterMarkerVisualLeft => 10 + CutterMarkerLeft;
    public double CutterMarkerContainerLeft => CutterMarkerVisualLeft - CutterMarkerContainerHalfWidth;

    partial void OnActiveToolChanged(TimelineTool value)
    {
        OnPropertyChanged(nameof(IsMouseToolActive));
        OnPropertyChanged(nameof(IsCutterToolActive));

        if (value != TimelineTool.Cutter)
        {
            IsCutterMarkerVisible = false;
        }
    }

    partial void OnCutterMarkerSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(CutterMarkerLeft));
        OnPropertyChanged(nameof(CutterMarkerVisualLeft));
        OnPropertyChanged(nameof(CutterMarkerContainerLeft));
    }

    [RelayCommand]
    private void SelectMouseTool()
    {
        ActiveTool = TimelineTool.Mouse;
    }

    [RelayCommand]
    private void SelectCutterTool()
    {
        ActiveTool = TimelineTool.Cutter;
    }

    [RelayCommand]
    private void AddNewLine()
    {
        if (!CanAddNewLine)
        {
            return;
        }

        var usedIndexes = new HashSet<int>(VideoLanes
            .Select(ResolveVideoLaneOrdinal)
            .Where(index => index > 1));

        var nextIndex = 2;
        while (usedIndexes.Contains(nextIndex))
        {
            nextIndex++;
        }

        VideoLanes.Insert(0, new VideoLaneItem($"VIDEO {nextIndex}", false, false, false));
    }

    [RelayCommand]
    private void RemoveLine(VideoLaneItem? lane)
    {
        if (lane is null || !CanRemoveLine(lane))
        {
            return;
        }

        var fallbackLane = ResolvePrimaryVideoLane();
        if (fallbackLane is null)
        {
            return;
        }

        var movedVideo = 0;
        foreach (var clip in VideoClips.Where(clip => string.Equals(clip.VideoLaneLabel, lane.Label, StringComparison.Ordinal)))
        {
            clip.VideoLaneLabel = fallbackLane.Label;
            movedVideo++;
        }

        var movedAudio = 0;
        foreach (var audioClip in AudioClips.Where(clip => string.Equals(clip.VideoLaneLabel, lane.Label, StringComparison.Ordinal)))
        {
            audioClip.VideoLaneLabel = fallbackLane.Label;
            movedAudio++;
        }

        VideoLanes.Remove(lane);
    }

    private bool CanRemoveLine(VideoLaneItem? lane)
    {
        return lane is { IsPrimary: false } && VideoLanes.Count > 1;
    }

    private static int ResolveVideoLaneOrdinal(VideoLaneItem lane)
    {
        if (lane.IsPrimary || string.Equals(lane.Label, "VIDEO", StringComparison.Ordinal))
        {
            return 1;
        }

        var suffix = lane.Label.Replace("VIDEO", string.Empty, StringComparison.Ordinal).Trim();
        return int.TryParse(suffix, out var parsed) && parsed > 1 ? parsed : int.MaxValue;
    }

    [RelayCommand]
    private static void ToggleVideoSolo(VideoLaneItem? lane)
    {
        if (lane is null)
        {
            return;
        }

        lane.IsSolo = !lane.IsSolo;
    }

    [RelayCommand]
    private static void ToggleVideoHidden(VideoLaneItem? lane)
    {
        if (lane is null)
        {
            return;
        }

        lane.IsHidden = !lane.IsHidden;
    }

    [RelayCommand]
    private static void ToggleAudioSolo(AudioLaneItem? lane)
    {
        if (lane is null)
        {
            return;
        }

        lane.IsSolo = !lane.IsSolo;
    }

    [RelayCommand]
    private static void ToggleAudioMuted(AudioLaneItem? lane)
    {
        if (lane is null)
        {
            return;
        }

        lane.IsMuted = !lane.IsMuted;
    }

    public bool TryCutAtPlayhead()
    {
        if (VideoClips.Count == 0)
        {
            return false;
        }

        var cutSeconds = Math.Clamp(PlayheadSeconds, 0, TimelineDurationSeconds);
        var targetClip = VideoClips.FirstOrDefault(clip =>
            cutSeconds > clip.StartSeconds + MinClipFragmentSeconds
            && cutSeconds < clip.StartSeconds + clip.DurationSeconds - MinClipFragmentSeconds);

        if (targetClip is null)
        {
            return false;
        }

        var previousVideo = CaptureClipSnapshots(VideoClips);
        var previousAudio = CaptureClipSnapshots(AudioClips);
        var previousPlayhead = PlayheadSeconds;

        ExecuteClipBatchUpdate(() => SplitVideoClip(targetClip, cutSeconds), cutSeconds);

        undoStack.Push(() => RestoreClipSnapshots(previousVideo, previousAudio, previousPlayhead));
        return true;
    }

    public void UpdateCutterMarkerFromCanvas(double pointerX, bool visible)
    {
        if (!IsCutterToolActive || !visible)
        {
            IsCutterMarkerVisible = false;
            return;
        }

        var markerSeconds = ResolveSnappedCutterSeconds(pointerX);
        CutterMarkerSeconds = markerSeconds;
        IsCutterMarkerVisible = true;
    }

    public void SeekCutterToCanvasPosition(double pointerX)
    {
        var clampedSeconds = ResolveSnappedCutterSeconds(pointerX);
        PlayheadSeconds = clampedSeconds;
        lastPlaybackMilliseconds = -1;
        PlaybackSeekRequested?.Invoke(ResolvePlaybackSeekMilliseconds(clampedSeconds));
        CutterMarkerSeconds = clampedSeconds;
    }

    public void HideCutterMarker()
    {
        IsCutterMarkerVisible = false;
    }

    private double ResolveSnappedCutterSeconds(double pointerX)
    {
        var rawSeconds = Math.Clamp((pointerX - 10) / TickWidth, 0, TimelineDurationSeconds);
        if (VideoClips.Count == 0)
        {
            return rawSeconds;
        }

        var thresholdSeconds = CutterSnapThresholdPixels / TickWidth;
        var nearestEdge = rawSeconds;
        var nearestDistance = double.MaxValue;

        foreach (var clip in VideoClips)
        {
            var startDistance = Math.Abs(rawSeconds - clip.StartSeconds);
            if (startDistance < nearestDistance)
            {
                nearestDistance = startDistance;
                nearestEdge = clip.StartSeconds;
            }

            var endSeconds = clip.StartSeconds + clip.DurationSeconds;
            var endDistance = Math.Abs(rawSeconds - endSeconds);
            if (endDistance < nearestDistance)
            {
                nearestDistance = endDistance;
                nearestEdge = endSeconds;
            }
        }

        if (nearestDistance <= thresholdSeconds)
        {
            return Math.Clamp(nearestEdge, 0, TimelineDurationSeconds);
        }

        return rawSeconds;
    }

    private void SplitVideoClip(TimelineClipItem targetClip, double cutSeconds)
    {
        var clipIndex = VideoClips.IndexOf(targetClip);
        if (clipIndex < 0)
        {
            return;
        }

        var leftDuration = cutSeconds - targetClip.StartSeconds;
        var rightDuration = (targetClip.StartSeconds + targetClip.DurationSeconds) - cutSeconds;

        if (leftDuration <= MinClipFragmentSeconds || rightDuration <= MinClipFragmentSeconds)
        {
            return;
        }

        var leftClip = targetClip.Clone();
        leftClip.DurationSeconds = leftDuration;

        var rightClip = targetClip.Clone();
        rightClip.StartSeconds = cutSeconds;
        rightClip.DurationSeconds = rightDuration;
        rightClip.SourceStartSeconds = targetClip.SourceStartSeconds + leftDuration;

        VideoClips.RemoveAt(clipIndex);
        VideoClips.Insert(clipIndex, leftClip);
        VideoClips.Insert(clipIndex + 1, rightClip);

        var originalLinkId = targetClip.LinkId;
        var targetAudio = AudioClips.FirstOrDefault(a => a.LinkId == originalLinkId);
        if (targetAudio != null)
        {
            var audioIndex = AudioClips.IndexOf(targetAudio);
            
            var leftAudio = targetAudio.Clone(leftClip.LinkId);
            leftAudio.DurationSeconds = leftDuration;

            var rightAudio = targetAudio.Clone(rightClip.LinkId);
            rightAudio.StartSeconds = cutSeconds;
            rightAudio.DurationSeconds = rightDuration;
            rightAudio.SourceStartSeconds = targetAudio.SourceStartSeconds + leftDuration;

            AudioClips.RemoveAt(audioIndex);
            AudioClips.Insert(audioIndex, leftAudio);
            AudioClips.Insert(audioIndex + 1, rightAudio);
        }
    }

    private void ExecuteClipBatchUpdate(Action updateAction, double targetPlayheadSeconds)
    {
        isBatchUpdatingClips = true;
        try
        {
            updateAction();
        }
        finally
        {
            isBatchUpdatingClips = false;
        }

        OnPropertyChanged(nameof(HasClips));
        PreviewClipChanged?.Invoke();

        if (VideoClips.Count == 0)
        {
            AudioClips.Clear();
            PlayheadSeconds = 0;
            RebuildLaneClipCollections();
            RebuildAudioLaneClipCollections();
            RefreshClipLevelLines();
            UpdatePreviewLevels();
            return;
        }

        RebuildLaneClipCollections();
        RebuildAudioFromVideo();
        PlayheadSeconds = Math.Clamp(targetPlayheadSeconds, 0, TimelineDurationSeconds);
        UpdatePreviewLevels();
    }

    private static List<TimelineClipItem> CaptureClipSnapshots(IEnumerable<TimelineClipItem> clips)
    {
        return clips.Select(clip => clip.Clone()).ToList();
    }

    private void RestoreClipSnapshots(List<TimelineClipItem> videoSnapshots, List<TimelineClipItem> audioSnapshots, double playheadSeconds)
    {
        ExecuteClipBatchUpdate(() =>
        {
            VideoClips.Clear();
            foreach (var snapshot in videoSnapshots)
            {
                VideoClips.Add(snapshot.Clone());
            }
        }, playheadSeconds);

        ApplyAudioVolumes(audioSnapshots);
        RefreshClipLevelLines();
        UpdatePreviewLevels();
    }

    private void ApplyAudioVolumes(IEnumerable<TimelineClipItem> audioSnapshots)
    {
        var volumeByKey = audioSnapshots.ToDictionary(BuildClipKey, snapshot => snapshot.VolumeLevel);
        foreach (var audioClip in AudioClips)
        {
            var clipKey = BuildClipKey(audioClip);
            if (volumeByKey.TryGetValue(clipKey, out var volumeLevel))
            {
                audioClip.VolumeLevel = volumeLevel;
            }
        }
    }

    private static string BuildClipKey(TimelineClipItem snapshot)
    {
        return BuildClipKey(
            snapshot.Name,
            snapshot.Path,
            snapshot.StartSeconds,
            snapshot.DurationSeconds,
            snapshot.SourceStartSeconds,
            snapshot.SourceDurationSeconds);
    }

    private static string BuildClipKey(
        string name,
        string path,
        double startSeconds,
        double durationSeconds,
        double sourceStartSeconds,
        double sourceDurationSeconds)
    {
        return $"{path}|{startSeconds:F3}|{durationSeconds:F3}|{sourceStartSeconds:F3}|{sourceDurationSeconds:F3}|{name}";
    }
}

public enum TimelineTool
{
    Mouse,
    Cutter
}
