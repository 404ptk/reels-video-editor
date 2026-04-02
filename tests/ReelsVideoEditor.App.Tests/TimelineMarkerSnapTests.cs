using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.Tests;

public class TimelineMarkerSnapTests
{
    [Fact]
    public void SeekToPosition_ClickNearClipStart_SnapsToClipStart()
    {
        var viewModel = new TimelineViewModel();
        var clip = new TimelineClipItem("clip", "sample.mp4", 10, 5)
        {
            VideoLaneLabel = "VIDEO"
        };
        viewModel.VideoClips.Add(clip);

        var pointerX = 10 + (10.5 * viewModel.TickWidth);
        viewModel.SeekToPosition(pointerX);

        Assert.Equal(10, viewModel.PlayheadSeconds, precision: 3);
    }

    [Fact]
    public void SeekToPosition_ClickFarFromClipEdges_DoesNotSnap()
    {
        var viewModel = new TimelineViewModel();
        var clip = new TimelineClipItem("clip", "sample.mp4", 10, 5)
        {
            VideoLaneLabel = "VIDEO"
        };
        viewModel.VideoClips.Add(clip);

        var pointerX = 10 + (12.2 * viewModel.TickWidth);
        viewModel.SeekToPosition(pointerX);

        Assert.Equal(12.2, viewModel.PlayheadSeconds, precision: 3);
    }
}
