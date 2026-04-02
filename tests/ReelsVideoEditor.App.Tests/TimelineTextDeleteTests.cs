using ReelsVideoEditor.App.Models;
using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.Tests;

public class TimelineTextDeleteTests
{
    [Fact]
    public void DeleteSelectedClips_WithTwoTextClips_DoesNotCreateAudioForRemainingText()
    {
        var viewModel = new TimelineViewModel();
        var preset = new TextPresetDefinition("Sunset", "Inter", 56, "#FFFFFF");
        viewModel.AddTextPresetClip(preset, dropX: 0);
        viewModel.AddTextPresetClip(preset, dropX: 100);

        Assert.Equal(2, viewModel.VideoClips.Count);
        Assert.Empty(viewModel.AudioClips);

        viewModel.VideoClips[0].IsSelected = true;
        viewModel.DeleteSelectedClips();

        Assert.Single(viewModel.VideoClips);
        Assert.Empty(viewModel.AudioClips);
    }
}
