using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.Tests;

public class TimelineClipArrangementZoomOutTests
{
    [Fact]
    public void RebuildLayouts_AtLowTickWidth_DoesNotCreateArtificialOverlapForAdjacentClips()
    {
        const double tickWidth = 3.5;

        var first = new TimelineClipItem("a", "a.mp4", startSeconds: 0, durationSeconds: 1, sourceDurationSeconds: 1);
        var second = new TimelineClipItem("b", "b.mp4", startSeconds: 1, durationSeconds: 1, sourceDurationSeconds: 1);

        TimelineClipArrangementService.RebuildLayouts([first, second], tickWidth);

        Assert.Equal(first.StartSeconds * tickWidth, first.Left, precision: 6);
        Assert.Equal(first.DurationSeconds * tickWidth, first.Width, precision: 6);
        Assert.Equal(second.StartSeconds * tickWidth, second.Left, precision: 6);
        Assert.Equal(second.DurationSeconds * tickWidth, second.Width, precision: 6);

        var firstRight = first.Left + first.Width;
        Assert.True(firstRight <= second.Left + 0.000001, "Adjacent clips should not visually overlap due to artificial minimum width.");
    }
}
