using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.Tests;

public class TimelineImageClipResizeTests
{
    [Fact]
    public void ResizeClipFromRight_AllowsImageClipToExtendBeyondInitialDuration()
    {
        var viewModel = new TimelineViewModel();
        viewModel.AddClipFromExplorer("image", "image.png", durationSeconds: 5, dropX: 0);

        var clip = Assert.Single(viewModel.VideoClips);
        Assert.Equal(5, clip.DurationSeconds);

        viewModel.ResizeClipFromRight(clip, requestedEndSeconds: 30);

        Assert.Equal(30, clip.DurationSeconds);
    }
}
