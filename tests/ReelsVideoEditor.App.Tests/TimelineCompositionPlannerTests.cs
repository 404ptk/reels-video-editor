using System.Collections.ObjectModel;
using ReelsVideoEditor.App.Services.Composition;
using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.Timeline.Arrangement;

namespace ReelsVideoEditor.App.Tests;

public class TimelineCompositionPlannerTests
{
    [Fact]
    public void BuildPlan_UsesHiddenAndSoloRulesConsistently()
    {
        var planner = new TimelineCompositionPlanner();
        var lanes = new ObservableCollection<VideoLaneItem>
        {
            new("VIDEO", true, false, false),
            new("VIDEO 2", false, true, false),
            new("VIDEO 3", false, false, true)
        };
        var clips = new ObservableCollection<TimelineClipItem>
        {
            BuildClip("base", "VIDEO", 0, 3),
            BuildClip("solo", "VIDEO 2", 0, 3),
            BuildClip("hidden", "VIDEO 3", 0, 3)
        };

        var plan = planner.BuildPlan(clips, lanes);

        Assert.Single(plan.VisibleVideoClips);
        Assert.Equal("solo", plan.VisibleVideoClips[0].Clip.Name);
    }

    [Fact]
    public void BuildExportVideoInputs_RespectsLaneOrderFromPlan()
    {
        var planner = new TimelineCompositionPlanner();
        var lanes = new ObservableCollection<VideoLaneItem>
        {
            new("VIDEO", true, false, false),
            new("VIDEO 2", false, false, false)
        };
        var clips = new ObservableCollection<TimelineClipItem>
        {
            BuildClip("bottom", "VIDEO", 0, 5),
            BuildClip("top", "VIDEO 2", 0, 5)
        };

        var plan = planner.BuildPlan(clips, lanes);
        var exportInputs = planner.BuildExportVideoInputs(plan);

        Assert.Equal(2, exportInputs.Count);
        Assert.Equal("top", exportInputs[0].Path);
        Assert.Equal(1, exportInputs[0].LaneOrder);
        Assert.Equal("bottom", exportInputs[1].Path);
        Assert.Equal(0, exportInputs[1].LaneOrder);
    }

    [Fact]
    public void ResolvePlaybackDurationSeconds_UsesVisibleVideoAndAudio()
    {
        var planner = new TimelineCompositionPlanner();
        var lanes = new ObservableCollection<VideoLaneItem>
        {
            new("VIDEO", true, false, false),
            new("VIDEO 2", false, false, true)
        };
        var videos = new ObservableCollection<TimelineClipItem>
        {
            BuildClip("v1", "VIDEO", 0, 2),
            BuildClip("v2_hidden", "VIDEO 2", 0, 12)
        };
        var audios = new ObservableCollection<TimelineClipItem>
        {
            BuildClip("a1", "AUDIO", 0, 4)
        };

        var plan = planner.BuildPlan(videos, lanes);

        var unmutedDuration = planner.ResolvePlaybackDurationSeconds(plan, audios, isAudioMuted: false, timelineDurationSeconds: 300);
        var mutedDuration = planner.ResolvePlaybackDurationSeconds(plan, audios, isAudioMuted: true, timelineDurationSeconds: 300);

        Assert.Equal(4, unmutedDuration);
        Assert.Equal(2, mutedDuration);
    }

    private static TimelineClipItem BuildClip(string name, string laneLabel, double startSeconds, double durationSeconds)
    {
        return new TimelineClipItem(name, name, startSeconds, durationSeconds)
        {
            VideoLaneLabel = laneLabel,
            TransformScale = 1.0
        };
    }
}
