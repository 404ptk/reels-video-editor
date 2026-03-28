using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReelsVideoEditor.App.ViewModels.Preview;
using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.Services.Export;

namespace ReelsVideoEditor.App.ViewModels.Export;

public sealed partial class ExportViewModel : ViewModelBase
{
    public string Title { get; } = "Export";

    public string Description { get; } = "Export your video with custom settings and effects";

    public Action<string, string>? ShowMessage { get; set; }

    public Func<Task<IStorageFolder?>>? RequestDirectory { get; set; }

    public TimelineViewModel? TimelineContext { get; set; }
    
    public PreviewViewModel? PreviewContext { get; set; }

    [ObservableProperty]
    private string outputName = "MyReel";

    [ObservableProperty]
    private string outputPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    [ObservableProperty]
    private string selectedFormat = "9:16";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableResolutions))]
    private string selectedResolution = "1080x1920";

    [ObservableProperty]
    private bool isExporting;

    [ObservableProperty]
    private double exportProgress;

    public ObservableCollection<string> AvailableFormats { get; } = new() { "9:16", "16:9" };

    public ObservableCollection<string> AvailableResolutions => SelectedFormat == "9:16"
        ? new ObservableCollection<string> { "1080x1920", "720x1280", "2160x3840" }
        : new ObservableCollection<string> { "1920x1080", "1280x720", "3840x2160" };

    partial void OnSelectedFormatChanged(string value)
    {
        OnPropertyChanged(nameof(AvailableResolutions));
        SelectedResolution = AvailableResolutions.FirstOrDefault() ?? "";
    }

    [RelayCommand]
    private async Task BrowseDirectoryAsync()
    {
        if (RequestDirectory == null) return;
        var folder = await RequestDirectory();
        if (folder != null)
        {
            OutputPath = folder.Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task ExportProjectAsync()
    {
        if (TimelineContext == null || !TimelineContext.HasClips)
        {
            ShowMessage?.Invoke("Empty Timeline", "There are no clips to export on the timeline.");
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputName) || string.IsNullOrWhiteSpace(OutputPath))
        {
            ShowMessage?.Invoke("Invalid Path", "Please specify a valid output name and directory.");
            return;
        }

        var fullPath = Path.Combine(OutputPath, $"{OutputName}.mp4");

        IsExporting = true;
        ExportProgress = 0;

        try
        {
            var exportAudios = TimelineContext.ResolveExportAudioInputs();
            var playbackDurationMilliseconds = TimelineContext.ResolvePlaybackDurationMilliseconds();

            var previewFrameWidth = Math.Max(1, PreviewContext?.PreviewFrameWidth ?? 1);
            var previewFrameHeight = Math.Max(1, PreviewContext?.PreviewFrameHeight ?? 1);

            var exporter = new Services.Export.TimelineExportService();
            await exporter.ExportAccurateAsync(
                exportAudios,
                TimelineContext.IsAudioMuted,
                fullPath,
                SelectedResolution,
                previewFrameWidth,
                previewFrameHeight,
                playbackDurationMilliseconds,
                playbackMilliseconds => TimelineContext.ResolvePreviewVideoLayers(playbackMilliseconds),
                playbackMilliseconds => TimelineContext.ResolveTextOverlayStateAt(playbackMilliseconds),
                new Progress<double>(p => ExportProgress = p));
            
            ShowMessage?.Invoke("Export Complete", $"Video successfully exported to:\n{fullPath}");
        }
        catch (Exception ex)
        {
            ShowMessage?.Invoke("Export Failed", $"An error occurred during export:\n{ex.Message}");
        }
        finally
        {
            IsExporting = false;
        }
    }
}
