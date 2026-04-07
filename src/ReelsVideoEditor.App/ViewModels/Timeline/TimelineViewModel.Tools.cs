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

        var leftClip = BuildClipFragment(
            targetClip,
            targetClip.StartSeconds,
            leftDuration,
            targetClip.SourceStartSeconds,
            targetClip.SourceDurationSeconds);
        var rightClip = BuildClipFragment(
            targetClip,
            cutSeconds,
            rightDuration,
            targetClip.SourceStartSeconds + leftDuration,
            targetClip.SourceDurationSeconds);

        VideoClips.RemoveAt(clipIndex);
        VideoClips.Insert(clipIndex, leftClip);
        VideoClips.Insert(clipIndex + 1, rightClip);
    }

    private TimelineClipItem BuildClipFragment(
        TimelineClipItem source,
        double startSeconds,
        double durationSeconds,
        double sourceStartSeconds,
        double sourceDurationSeconds)
    {
        var clip = new TimelineClipItem(
            source.Name,
            source.Path,
            startSeconds,
            durationSeconds,
            null,
            sourceStartSeconds,
            sourceDurationSeconds)
        {
            IsSelected = source.IsSelected,
            VolumeLevel = source.VolumeLevel,
            VideoLaneLabel = source.VideoLaneLabel,
            TransformX = source.TransformX,
            TransformY = source.TransformY,
            TransformScale = source.TransformScale,
            CropLeft = source.CropLeft,
            CropTop = source.CropTop,
            CropRight = source.CropRight,
            CropBottom = source.CropBottom,
            TextContent = source.TextContent,
            TextColorHex = source.TextColorHex,
            TextOutlineColorHex = source.TextOutlineColorHex,
            TextOutlineThickness = source.TextOutlineThickness,
            TextFontSize = source.TextFontSize,
            TextFontFamily = source.TextFontFamily,
            TextLineHeightMultiplier = source.TextLineHeightMultiplier,
            TextLetterSpacing = source.TextLetterSpacing,
            TextRevealEffect = source.TextRevealEffect
        };

        clip.Left = clip.StartSeconds * TickWidth;
        clip.Width = Math.Max(24, clip.DurationSeconds * TickWidth);
        return clip;
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

    private static List<ClipSnapshot> CaptureClipSnapshots(IEnumerable<TimelineClipItem> clips)
    {
        return clips.Select(clip => new ClipSnapshot(
            clip.Name,
            clip.Path,
            clip.StartSeconds,
            clip.DurationSeconds,
            clip.SourceStartSeconds,
            clip.SourceDurationSeconds,
            clip.VolumeLevel,
            clip.IsSelected,
            clip.VideoLaneLabel,
            clip.TransformX,
            clip.TransformY,
            clip.TransformScale,
            clip.CropLeft,
            clip.CropTop,
            clip.CropRight,
            clip.CropBottom,
            clip.TextContent,
            clip.TextColorHex,
            clip.TextOutlineColorHex,
            clip.TextOutlineThickness,
            clip.TextFontSize,
            clip.TextFontFamily,
            clip.TextLineHeightMultiplier,
            clip.TextLetterSpacing,
            clip.TextRevealEffect)).ToList();
    }

    private void RestoreClipSnapshots(List<ClipSnapshot> videoSnapshots, List<ClipSnapshot> audioSnapshots, double playheadSeconds)
    {
        ExecuteClipBatchUpdate(() =>
        {
            VideoClips.Clear();
            foreach (var snapshot in videoSnapshots)
            {
                VideoClips.Add(BuildClipFromSnapshot(snapshot));
            }
        }, playheadSeconds);

        ApplyAudioVolumes(audioSnapshots);
        RefreshClipLevelLines();
        UpdatePreviewLevels();
    }

    private TimelineClipItem BuildClipFromSnapshot(ClipSnapshot snapshot)
    {
        var clip = new TimelineClipItem(
            snapshot.Name,
            snapshot.Path,
            snapshot.StartSeconds,
            snapshot.DurationSeconds,
            null,
            snapshot.SourceStartSeconds,
            snapshot.SourceDurationSeconds)
        {
            VolumeLevel = snapshot.VolumeLevel,
            IsSelected = snapshot.IsSelected,
            VideoLaneLabel = snapshot.VideoLaneLabel,
            TransformX = snapshot.TransformX,
            TransformY = snapshot.TransformY,
            TransformScale = snapshot.TransformScale,
            CropLeft = snapshot.CropLeft,
            CropTop = snapshot.CropTop,
            CropRight = snapshot.CropRight,
            CropBottom = snapshot.CropBottom,
            TextContent = snapshot.TextContent,
            TextColorHex = snapshot.TextColorHex,
            TextOutlineColorHex = snapshot.TextOutlineColorHex,
            TextOutlineThickness = snapshot.TextOutlineThickness,
            TextFontSize = snapshot.TextFontSize,
            TextFontFamily = snapshot.TextFontFamily,
            TextLineHeightMultiplier = snapshot.TextLineHeightMultiplier,
            TextLetterSpacing = snapshot.TextLetterSpacing,
            TextRevealEffect = snapshot.TextRevealEffect
        };

        clip.Left = clip.StartSeconds * TickWidth;
        clip.Width = Math.Max(24, clip.DurationSeconds * TickWidth);
        return clip;
    }

    private void ApplyAudioVolumes(IEnumerable<ClipSnapshot> audioSnapshots)
    {
        var volumeByKey = audioSnapshots.ToDictionary(BuildClipKey, snapshot => snapshot.VolumeLevel);
        foreach (var audioClip in AudioClips)
        {
            var clipKey = BuildClipKey(
                audioClip.Name,
                audioClip.Path,
                audioClip.StartSeconds,
                audioClip.DurationSeconds,
                audioClip.SourceStartSeconds,
                audioClip.SourceDurationSeconds);
            if (volumeByKey.TryGetValue(clipKey, out var volumeLevel))
            {
                audioClip.VolumeLevel = volumeLevel;
            }
        }
    }

    private static string BuildClipKey(ClipSnapshot snapshot)
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

    private readonly record struct ClipSnapshot(
        string Name,
        string Path,
        double StartSeconds,
        double DurationSeconds,
        double SourceStartSeconds,
        double SourceDurationSeconds,
        double VolumeLevel,
        bool IsSelected,
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
}

public enum TimelineTool
{
    Mouse,
    Cutter
}
