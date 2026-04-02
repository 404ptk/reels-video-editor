using System.Linq;
using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.Tests;

public class TimelineCopyPasteTests
{
    [Fact]
    public void PasteCopiedClipAtPlayhead_SequentialPaste_StartsAtEndOfPreviousPaste()
    {
        var viewModel = new TimelineViewModel();
        var source = new TimelineClipItem("text", string.Empty, 0, 5)
        {
            VideoLaneLabel = "VIDEO",
            IsSelected = true
        };

        viewModel.VideoClips.Add(source);
        viewModel.SelectSingleVideoClip(source);
        viewModel.CopySelectedClip();

        viewModel.PlayheadSeconds = 10;
        var firstPaste = viewModel.PasteCopiedClipAtPlayhead();
        var secondPaste = viewModel.PasteCopiedClipAtPlayhead();

        Assert.True(firstPaste);
        Assert.True(secondPaste);
        Assert.Equal(3, viewModel.VideoClips.Count);
        Assert.Equal(10, viewModel.VideoClips[1].StartSeconds);
        Assert.Equal(15, viewModel.VideoClips[2].StartSeconds);
        Assert.Equal(20, viewModel.PlayheadSeconds);
    }

    [Fact]
    public void PasteCopiedClipAtPlayhead_AfterManualPlayheadMove_UsesCurrentPlayheadAsStart()
    {
        var viewModel = new TimelineViewModel();
        var source = new TimelineClipItem("text", string.Empty, 0, 5)
        {
            VideoLaneLabel = "VIDEO",
            IsSelected = true
        };

        viewModel.VideoClips.Add(source);
        viewModel.SelectSingleVideoClip(source);
        viewModel.CopySelectedClip();

        Assert.True(viewModel.PasteCopiedClipAtPlayhead());

        viewModel.PlayheadSeconds = 30;
        Assert.True(viewModel.PasteCopiedClipAtPlayhead());

        Assert.Equal(3, viewModel.VideoClips.Count);
        Assert.Equal(0, viewModel.VideoClips[1].StartSeconds);
        Assert.Equal(30, viewModel.VideoClips[2].StartSeconds);
        Assert.Equal(35, viewModel.PlayheadSeconds);
    }

    [Fact]
    public void PasteCopiedClipAtPlayhead_WithVideoClip_RefreshesAudioLaneCollections()
    {
        var viewModel = new TimelineViewModel();
        var source = new TimelineClipItem("video", "sample.mp4", 0, 4)
        {
            VideoLaneLabel = "VIDEO",
            IsSelected = true
        };

        viewModel.VideoClips.Add(source);
        viewModel.SelectSingleVideoClip(source);
        viewModel.CopySelectedClip();

        var laneClipCountBefore = viewModel.AudioLanes.Sum(lane => lane.Clips.Count);
        var audioCountBefore = viewModel.AudioClips.Count;

        viewModel.PlayheadSeconds = 8;
        Assert.True(viewModel.PasteCopiedClipAtPlayhead());

        var laneClipCountAfter = viewModel.AudioLanes.Sum(lane => lane.Clips.Count);

        Assert.Equal(audioCountBefore + 1, viewModel.AudioClips.Count);
        Assert.Equal(viewModel.AudioClips.Count, laneClipCountAfter);
        Assert.True(laneClipCountAfter > laneClipCountBefore);
    }
}
