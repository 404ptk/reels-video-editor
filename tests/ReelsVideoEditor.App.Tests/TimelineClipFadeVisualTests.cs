using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.Tests;

public class TimelineClipFadeVisualTests
{
    [Fact]
    public void FadeVisualWidths_FollowDurationRatios()
    {
        var clip = new TimelineClipItem("clip", "path", 0, 10)
        {
            Width = 200
        };

        clip.FadeInDurationSeconds = 2;
        clip.FadeOutDurationSeconds = 3;

        Assert.Equal(40, clip.FadeInVisualWidth, 3);
        Assert.Equal(60, clip.FadeOutVisualWidth, 3);
        Assert.True(clip.IsFadeInVisualVisible);
        Assert.True(clip.IsFadeOutVisualVisible);
    }

    [Fact]
    public void FadeVisualWidths_UpdateWhenClipWidthChanges()
    {
        var clip = new TimelineClipItem("clip", "path", 0, 8)
        {
            Width = 80,
            FadeInDurationSeconds = 2,
            FadeOutDurationSeconds = 2
        };

        Assert.Equal(20, clip.FadeInVisualWidth, 3);
        Assert.Equal(20, clip.FadeOutVisualWidth, 3);

        clip.Width = 160;

        Assert.Equal(40, clip.FadeInVisualWidth, 3);
        Assert.Equal(40, clip.FadeOutVisualWidth, 3);
    }
}
