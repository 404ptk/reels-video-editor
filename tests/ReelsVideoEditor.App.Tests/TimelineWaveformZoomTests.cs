using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.Tests;

public class TimelineWaveformZoomTests
{
    [Fact]
    public void ChangeZoomFromWheel_RecomputesAudioWaveformVisualWidth()
    {
        var viewModel = new TimelineViewModel();
        viewModel.AddClipFromExplorer("clip", "clip.mp4", durationSeconds: 30, dropX: 0);

        var audioClip = Assert.Single(viewModel.AudioClips);
        var initialClipWidth = audioClip.Width;
        var initialWaveformWidth = audioClip.AudioWaveformVisualWidth;

        viewModel.ChangeZoomFromWheel(wheelDelta: 1, viewportWidth: 1200);

        Assert.True(audioClip.Width > initialClipWidth);
        Assert.Equal(audioClip.Width, audioClip.AudioWaveformVisualWidth, precision: 6);
        Assert.NotEqual(initialWaveformWidth, audioClip.AudioWaveformVisualWidth);
    }
}
