using System;
using System.Collections.Generic;
using System.Reflection;
using ReelsVideoEditor.App.Services.SpeechTranscription;

namespace ReelsVideoEditor.App.Tests;

public class SpeechTranscriptionServiceTimingTests
{
    [Fact]
    public void SplitSegmentIntoWords_WithLongLeadingSilence_AnchorsWordsNearSegmentEnd()
    {
        var words = InvokeSplitSegmentIntoWords(
            "to bedzie pozniej",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5));

        Assert.Equal(3, words.Count);
        Assert.True(words[0].Start >= TimeSpan.FromSeconds(3.5));
        Assert.Equal(TimeSpan.FromSeconds(5), words[^1].End);
    }

    [Fact]
    public void SplitSegmentIntoWords_WithShortSegment_DoesNotShiftWordsPastSegmentStart()
    {
        var words = InvokeSplitSegmentIntoWords(
            "raz dwa trzy",
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(800));

        Assert.Equal(3, words.Count);
        Assert.Equal(TimeSpan.Zero, words[0].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(800), words[^1].End);
    }

    private static List<TranscriptionWord> InvokeSplitSegmentIntoWords(string text, TimeSpan segStart, TimeSpan segEnd)
    {
        var method = typeof(SpeechTranscriptionService).GetMethod(
            "SplitSegmentIntoWords",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(null, [text, segStart, segEnd]);
        var words = Assert.IsType<List<TranscriptionWord>>(result);
        return words;
    }
}
