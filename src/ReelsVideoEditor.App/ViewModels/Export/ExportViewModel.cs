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

    [ObservableProperty]
    private string statusTitle = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool hasStatusMessage;

    [ObservableProperty]
    private bool isStatusError;

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
    private void DismissStatus()
    {
        HasStatusMessage = false;
    }

    private void SetStatus(string title, string message, bool isError)
    {
        StatusTitle = title;
        StatusMessage = message;
        IsStatusError = isError;
        HasStatusMessage = true;
    }

    [RelayCommand]
    private async Task ExportProjectAsync()
    {
        HasStatusMessage = false;

        if (TimelineContext == null || !TimelineContext.HasClips)
        {
            SetStatus("Empty Timeline", "There are no clips to export on the timeline.", isError: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputName) || string.IsNullOrWhiteSpace(OutputPath))
        {
            SetStatus("Invalid Path", "Please specify a valid output name and directory.", isError: true);
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

            SetStatus("Export Complete", $"Video successfully exported to:\n{fullPath}", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus("Export Failed", $"An error occurred during export:\n{ex.Message}", isError: true);
        }
        finally
        {
            IsExporting = false;
        }
    }
}
