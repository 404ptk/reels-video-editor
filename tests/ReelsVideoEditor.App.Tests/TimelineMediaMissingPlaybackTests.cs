using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.Tests;

public class TimelineMediaMissingPlaybackTests
{
    [Fact]
    public void ResolvePreviewVideoLayers_IgnoresMediaMissingClip()
    {
        var viewModel = new TimelineViewModel();
        var clip = new TimelineClipItem("clip", "sample.mp4", 0, 5)
        {
            VideoLaneLabel = "VIDEO",
            IsMediaMissing = true
        };

        viewModel.VideoClips.Add(clip);

        var layers = viewModel.ResolvePreviewVideoLayers(1000);

        Assert.Empty(layers);
    }

    [Fact]
    public void ResolvePreviewAudioState_IgnoresMediaMissingAudioClip()
    {
        var viewModel = new TimelineViewModel();
        var audioClip = new TimelineClipItem("audio", "sample.mp4", 0, 5)
        {
            VideoLaneLabel = "VIDEO",
            IsMediaMissing = true
        };

        viewModel.AudioClips.Add(audioClip);

        var state = viewModel.ResolvePreviewAudioState(1000);

        Assert.False(state.ShouldPlay);
        Assert.Empty(state.Tracks);
    }
}
