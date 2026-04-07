using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.ViewModels.Timeline;

public sealed record TimelineMinorTick(bool ShowLine);

public sealed record TimelineMajorTick(string Label, double Width);

public sealed partial class VideoLaneItem : ObservableObject
{
    public VideoLaneItem(string label, bool isPrimary, bool isSolo, bool isHidden)
    {
        Label = label;
        IsPrimary = isPrimary;
        IsSolo = isSolo;
        IsHidden = isHidden;
    }

    public string Label { get; }

    public bool IsPrimary { get; }

    public ObservableCollection<TimelineClipItem> Clips { get; } = [];

    [ObservableProperty]
    private bool isSolo;

    [ObservableProperty]
    private bool isHidden;
}

public sealed partial class AudioLaneItem : ObservableObject
{
    public AudioLaneItem(string label, bool isPrimary, bool isSolo, bool isMuted)
    {
        Label = label;
        IsPrimary = isPrimary;
        IsSolo = isSolo;
        IsMuted = isMuted;
    }

    public string Label { get; }

    public bool IsPrimary { get; }

    public ObservableCollection<TimelineClipItem> Clips { get; } = [];

    [ObservableProperty]
    private bool isSolo;

    [ObservableProperty]
    private bool isMuted;
}

public sealed record PreviewVideoLayer(
    string Path,
    long PlaybackMilliseconds,
    bool DrawBlurredBackground,
    bool IsSelected,
    bool HasAnySelectedVideoClip,
    double TransformX,
    double TransformY,
    double TransformScale,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom);

public sealed record PreviewClipTransform(
    double TransformX,
    double TransformY,
    double TransformScale,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom)
{
    public static PreviewClipTransform Default { get; } = new(0, 0, 1, 0, 0, 0, 0);
}

public sealed record PreviewAudioTrackState(string TrackKey, string Path, long PlaybackMilliseconds, double VolumeLevel);

public sealed record PreviewAudioState(IReadOnlyList<PreviewAudioTrackState> Tracks, bool ShouldPlay)
{
    public static PreviewAudioState Silent { get; } = new(Array.Empty<PreviewAudioTrackState>(), false);
}

public sealed record TimelineTextOverlayLayer(
    string Text,
    string FontFamily,
    double FontSize,
    double LineHeightMultiplier,
    double LetterSpacing,
    string ColorHex,
    string OutlineColorHex,
    double OutlineThickness,
    double TransformX,
    double TransformY,
    double TransformScale,
    double AnimationScale,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom);

public sealed record TimelineTextOverlayState(
    IReadOnlyList<TimelineTextOverlayLayer> Layers)
{
    public bool IsVisible => Layers.Count > 0;
}

public sealed record TimelineSelectedTextClipState(
    bool HasSelection,
    string Text,
    string FontFamily,
    double FontSize,
    double LineHeightMultiplier,
    double LetterSpacing,
    string ColorHex,
    string OutlineColorHex,
    double OutlineThickness,
    string TextRevealEffect);
