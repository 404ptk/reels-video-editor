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

        var nextIndex = VideoLanes.Count + 1;
        var insertIndex = VideoLanes
            .Select((lane, index) => new { lane, index })
            .Where(x => x.lane.IsPrimary)
            .Select(x => x.index)
            .DefaultIfEmpty(-1)
            .First();
        if (insertIndex < 0)
        {
            insertIndex = VideoLanes.Count;
        }

        VideoLanes.Insert(insertIndex, new VideoLaneItem($"VIDEO {nextIndex}", false, false, false));
    }

    [RelayCommand]
    private void Placeholder1()
    {
    }

    [RelayCommand]
    private void Placeholder2()
    {
    }

    [RelayCommand]
    private void Placeholder3()
    {
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
        PlayheadSeekRequested?.Invoke(clampedSeconds);
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

        var leftClip = BuildClipFragment(targetClip, targetClip.StartSeconds, leftDuration);
        var rightClip = BuildClipFragment(targetClip, cutSeconds, rightDuration);

        VideoClips.RemoveAt(clipIndex);
        VideoClips.Insert(clipIndex, leftClip);
        VideoClips.Insert(clipIndex + 1, rightClip);
    }

    private TimelineClipItem BuildClipFragment(TimelineClipItem source, double startSeconds, double durationSeconds)
    {
        var clip = new TimelineClipItem(source.Name, source.Path, startSeconds, durationSeconds)
        {
            IsSelected = source.IsSelected,
            VolumeLevel = source.VolumeLevel
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
            hasPlaybackSession = false;
            RefreshClipLevelLines();
            UpdatePreviewLevels();
            return;
        }

        RebuildAudioFromVideo();
        PlayheadSeconds = Math.Clamp(targetPlayheadSeconds, 0, TimelineDurationSeconds);
        UpdatePreviewLevels();
    }

    private List<ClipSnapshot> CaptureClipSnapshots(IEnumerable<TimelineClipItem> clips)
    {
        return clips.Select(clip => new ClipSnapshot(
            clip.Name,
            clip.Path,
            clip.StartSeconds,
            clip.DurationSeconds,
            clip.VolumeLevel,
            clip.IsSelected)).ToList();
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
        var clip = new TimelineClipItem(snapshot.Name, snapshot.Path, snapshot.StartSeconds, snapshot.DurationSeconds)
        {
            VolumeLevel = snapshot.VolumeLevel,
            IsSelected = snapshot.IsSelected
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
            var clipKey = BuildClipKey(audioClip.Name, audioClip.Path, audioClip.StartSeconds, audioClip.DurationSeconds);
            if (volumeByKey.TryGetValue(clipKey, out var volumeLevel))
            {
                audioClip.VolumeLevel = volumeLevel;
            }
        }
    }

    private static string BuildClipKey(ClipSnapshot snapshot)
    {
        return BuildClipKey(snapshot.Name, snapshot.Path, snapshot.StartSeconds, snapshot.DurationSeconds);
    }

    private static string BuildClipKey(string name, string path, double startSeconds, double durationSeconds)
    {
        return $"{path}|{startSeconds:F3}|{durationSeconds:F3}|{name}";
    }

    private readonly record struct ClipSnapshot(
        string Name,
        string Path,
        double StartSeconds,
        double DurationSeconds,
        double VolumeLevel,
        bool IsSelected);
}

public enum TimelineTool
{
    Mouse,
    Cutter
}
