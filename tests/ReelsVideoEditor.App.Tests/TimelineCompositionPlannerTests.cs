using System.Collections.ObjectModel;
using System.Linq;
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

    [Fact]
    public void ResolvePlaybackDurationSeconds_IgnoresMediaMissingAudioClips()
    {
        var planner = new TimelineCompositionPlanner();
        var lanes = new ObservableCollection<VideoLaneItem>
        {
            new("VIDEO", true, false, false)
        };
        var videos = new ObservableCollection<TimelineClipItem>();
        var audios = new ObservableCollection<TimelineClipItem>
        {
            BuildClip("a_missing", "AUDIO", 0, 12, isMediaMissing: true),
            BuildClip("a_ok", "AUDIO", 0, 3)
        };

        var plan = planner.BuildPlan(videos, lanes);
        var duration = planner.ResolvePlaybackDurationSeconds(plan, audios, isAudioMuted: false, timelineDurationSeconds: 300);

        Assert.Equal(3, duration);
    }

    [Fact]
    public void ResolveActiveVideoLayers_AtClipEnd_UsesClampedSourceTimeForShortClip()
    {
        var planner = new TimelineCompositionPlanner();
        var lanes = new ObservableCollection<VideoLaneItem>
        {
            new("VIDEO", true, false, false)
        };
        var clips = new ObservableCollection<TimelineClipItem>
        {
            BuildClip("short", "VIDEO", 10, 0.25, sourceStartSeconds: 1.5)
        };

        var plan = planner.BuildPlan(clips, lanes);
        var layers = planner.ResolveActiveVideoLayers(plan, timelineSeconds: 10.25);

        Assert.Single(layers);
        Assert.Equal(1750, layers[0].PlaybackMilliseconds);
    }

    [Fact]
    public void ResolveActiveVideoLayers_UsesFadeDurationsToShapeOpacity()
    {
        var planner = new TimelineCompositionPlanner();
        var lanes = new ObservableCollection<VideoLaneItem>
        {
            new("VIDEO", true, false, false)
        };
        var clip = BuildClip("fade", "VIDEO", 10, 8);
        clip.FadeInDurationSeconds = 2;
        clip.FadeOutDurationSeconds = 2;
        clip.Opacity = 1.0;

        var plan = planner.BuildPlan(new ObservableCollection<TimelineClipItem> { clip }, lanes);

        var fadeInLayer = planner.ResolveActiveVideoLayers(plan, timelineSeconds: 11).Single();
        var middleLayer = planner.ResolveActiveVideoLayers(plan, timelineSeconds: 14).Single();
        var fadeOutLayer = planner.ResolveActiveVideoLayers(plan, timelineSeconds: 17).Single();

        Assert.Equal(0.5, fadeInLayer.Opacity, 3);
        Assert.Equal(1.0, middleLayer.Opacity, 3);
        Assert.Equal(0.5, fadeOutLayer.Opacity, 3);
    }

    private static TimelineClipItem BuildClip(
        string name,
        string laneLabel,
        double startSeconds,
        double durationSeconds,
        bool isMediaMissing = false,
        double sourceStartSeconds = 0)
    {
        return new TimelineClipItem(name, name, startSeconds, durationSeconds)
        {
            VideoLaneLabel = laneLabel,
            TransformScale = 1.0,
            IsMediaMissing = isMediaMissing,
            SourceStartSeconds = sourceStartSeconds
        };
    }
}
