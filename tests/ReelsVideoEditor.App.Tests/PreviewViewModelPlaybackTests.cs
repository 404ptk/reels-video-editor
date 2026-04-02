using ReelsVideoEditor.App.ViewModels.Preview;

namespace ReelsVideoEditor.App.Tests;

public class PreviewViewModelPlaybackTests
{
    [Fact]
    public void TogglePlayPause_StartsPlayback_WhenOnlySyntheticTimelineContentExists()
    {
        var viewModel = new PreviewViewModel
        {
            HasSyntheticVideoContent = () => true,
            ResolveVideoPath = () => null
        };

        viewModel.TogglePlayPauseCommand.Execute(null);

        Assert.True(viewModel.IsPlaying);
    }

    [Fact]
    public void TogglePlayPause_DoesNotStartPlayback_WhenNoVideoAndNoSyntheticContent()
    {
        var viewModel = new PreviewViewModel
        {
            HasSyntheticVideoContent = () => false,
            ResolveVideoPath = () => null
        };

        viewModel.TogglePlayPauseCommand.Execute(null);

        Assert.False(viewModel.IsPlaying);
    }

    [Fact]
    public void SeekToPlaybackPosition_UpdatesSeekRequest_WhenOnlySyntheticTimelineContentExists()
    {
        var viewModel = new PreviewViewModel
        {
            HasSyntheticVideoContent = () => true,
            ResolveVideoPath = () => null
        };

        viewModel.SeekToPlaybackPosition(1750);

        Assert.Equal(1750, viewModel.RequestedSeekMilliseconds);
        Assert.Equal(1, viewModel.SeekRequestVersion);
        Assert.Equal("00:01:75", viewModel.CurrentPlaybackTimeText);
    }
}
