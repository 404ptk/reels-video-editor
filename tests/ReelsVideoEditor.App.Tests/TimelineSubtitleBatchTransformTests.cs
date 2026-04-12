using System;
using ReelsVideoEditor.App.Models;
using ReelsVideoEditor.App.Services.SpeechTranscription;
using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.Tests;

public class TimelineSubtitleBatchTransformTests
{
    [Fact]
    public void ApplyTransformToTarget_InSubtitleBatchMode_UpdatesAllActiveSubtitleClips()
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
            new TranscriptionChunk("line 1", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2)),
            new TranscriptionChunk("line 2", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2.5))
        };

        viewModel.AddAutoCaptionClips(chunks, preset);
        viewModel.SetSubtitleBatchTransformEnabled(true);
        viewModel.UpdatePlayheadFromPlayback(1000);

        viewModel.ApplyTransformToTarget(
            transformX: 120,
            transformY: -64,
            transformScale: 1.2,
            cropLeft: 0.1,
            cropTop: 0.05,
            cropRight: 0.12,
            cropBottom: 0.08);

        Assert.Equal(2, viewModel.VideoClips.Count);
        foreach (var clip in viewModel.VideoClips)
        {
            Assert.True(clip.IsSubtitle);
            Assert.Equal(120, clip.TransformX, precision: 3);
            Assert.Equal(-64, clip.TransformY, precision: 3);
            Assert.Equal(1.2, clip.TransformScale, precision: 3);
            Assert.Equal(0.1, clip.CropLeft, precision: 3);
            Assert.Equal(0.05, clip.CropTop, precision: 3);
            Assert.Equal(0.12, clip.CropRight, precision: 3);
            Assert.Equal(0.08, clip.CropBottom, precision: 3);
        }
    }
}
