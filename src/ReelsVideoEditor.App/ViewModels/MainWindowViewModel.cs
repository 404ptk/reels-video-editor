using ReelsVideoEditor.App.ViewModels.Preview;
using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.VideoFiles;
using System;

namespace ReelsVideoEditor.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public PreviewViewModel Preview { get; }

    public VideoFilesViewModel VideoFiles { get; } = new();

    public TimelineViewModel Timeline { get; } = new();

    public MainWindowViewModel()
    {
        Preview = new PreviewViewModel
        {
            ResolveVideoPath = () => Timeline.ResolvePreviewClipPath(),
            PlaybackTimeChanged = playbackMilliseconds => Timeline.UpdatePlayheadFromPlayback(playbackMilliseconds),
            PlaybackStateChanged = isPlaying => Timeline.SetPlaybackActive(isPlaying)
        };

        Timeline.PlayheadSeekRequested = seconds =>
        {
            var seekMilliseconds = (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
            Preview.SeekToPlaybackPosition(seekMilliseconds);
        };
    }
}
