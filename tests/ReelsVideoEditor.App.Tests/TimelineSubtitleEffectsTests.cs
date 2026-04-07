using System;
using ReelsVideoEditor.App.Models;
using ReelsVideoEditor.App.Services.SpeechTranscription;
using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.Tests;

public class TimelineSubtitleEffectsTests
{
    [Fact]
    public void ResolveTextOverlayStateAt_WithPopEffect_UsesTemporaryScaleBoostAtClipStart()
    {
        var viewModel = new TimelineViewModel();
        var preset = new TextPresetDefinition(
            "Auto",
            "Inter",
            18,
            "#FFFFFF",
            "#000000",
            3,
            1.0,
            0,
            TextRevealEffect.Pop,
            IsAutoCaptions: true);
        var chunks = new[]
        {
            new TranscriptionChunk("line 1", TimeSpan.Zero, TimeSpan.FromSeconds(2))
        };

        viewModel.AddAutoCaptionClips(chunks, preset);

        var startState = viewModel.ResolveTextOverlayStateAt(0);
        var lateState = viewModel.ResolveTextOverlayStateAt(500);

        var startLayer = Assert.Single(startState.Layers);
        var lateLayer = Assert.Single(lateState.Layers);
        Assert.True(startLayer.AnimationScale > 1.0);
        Assert.Equal(1.0, lateLayer.AnimationScale, precision: 3);
    }

    [Fact]
    public void ResolveTextOverlayStateAt_WithNoEffect_UsesBaseScale()
    {
        var viewModel = new TimelineViewModel();
        var preset = new TextPresetDefinition(
            "Auto",
            "Inter",
            18,
            "#FFFFFF",
            "#000000",
            3,
            1.0,
            0,
            TextRevealEffect.None,
            IsAutoCaptions: true);
        var chunks = new[]
        {
            new TranscriptionChunk("line 1", TimeSpan.Zero, TimeSpan.FromSeconds(2))
        };

        viewModel.AddAutoCaptionClips(chunks, preset);

        var state = viewModel.ResolveTextOverlayStateAt(50);
        var layer = Assert.Single(state.Layers);
        Assert.Equal(1.0, layer.AnimationScale, precision: 3);
    }
}
