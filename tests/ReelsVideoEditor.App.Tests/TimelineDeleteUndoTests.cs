using System.Linq;
using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.Tests;

public class TimelineDeleteUndoTests
{
    [Fact]
    public void DeleteSelectedClips_Undo_DoesNotDuplicateLinkedAudio()
    {
        var viewModel = new TimelineViewModel();
        var videoClip = new TimelineClipItem("video", "sample.mp4", 5, 4)
        {
            VideoLaneLabel = "VIDEO"
        };
        var audioClip = TimelineClipArrangementService.BuildLinkedAudioClip(videoClip);

        viewModel.VideoClips.Add(videoClip);
        viewModel.AudioClips.Add(audioClip);
        viewModel.SelectSingleVideoClip(videoClip);

        viewModel.DeleteSelectedClips();

        Assert.Empty(viewModel.VideoClips);
        Assert.Empty(viewModel.AudioClips);

        viewModel.Undo();

        Assert.Single(viewModel.VideoClips);
        Assert.Single(viewModel.AudioClips);
        Assert.Equal(viewModel.AudioClips.Count, viewModel.AudioLanes.Sum(lane => lane.Clips.Count));
    }

    [Fact]
    public void DeleteSelectedClips_Undo_RestoresAudioVolume()
    {
        var viewModel = new TimelineViewModel();
        var videoClip = new TimelineClipItem("video", "sample.mp4", 0, 6)
        {
            VideoLaneLabel = "VIDEO"
        };
        var audioClip = TimelineClipArrangementService.BuildLinkedAudioClip(videoClip);
        audioClip.VolumeLevel = 0.35;

        viewModel.VideoClips.Add(videoClip);
        viewModel.AudioClips.Add(audioClip);
        viewModel.SelectSingleVideoClip(videoClip);

        viewModel.DeleteSelectedClips();
        viewModel.Undo();

        Assert.Single(viewModel.AudioClips);
        Assert.Equal(0.35, viewModel.AudioClips[0].VolumeLevel, 3);
    }
}
