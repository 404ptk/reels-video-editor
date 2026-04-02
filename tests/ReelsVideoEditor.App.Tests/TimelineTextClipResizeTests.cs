using ReelsVideoEditor.App.Models;
using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.Tests;

public class TimelineTextClipResizeTests
{
    [Fact]
    public void ResizeClipFromRight_AllowsTextClipToExtendBeyondInitialSourceDuration()
    {
        var viewModel = new TimelineViewModel();
        var preset = new TextPresetDefinition("Sunset", "Inter", 56, "#FFFFFF");
        viewModel.AddTextPresetClip(preset, dropX: 0);

        var clip = Assert.Single(viewModel.VideoClips);
        Assert.Equal(5, clip.DurationSeconds);

        viewModel.ResizeClipFromRight(clip, requestedEndSeconds: 30);

        Assert.Equal(30, clip.DurationSeconds);
    }
}
