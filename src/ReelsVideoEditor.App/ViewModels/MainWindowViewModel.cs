namespace ReelsVideoEditor.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public string Title { get; } = "Reels Video Editor";

    public string SourceStatus { get; } = "No file selected yet — the UI shell is ready for the next implementation step.";

    public string InputAspectRatio { get; } = "16:9";

    public string OutputAspectRatio { get; } = "9:16";

    public string BackgroundMode { get; } = "Blurred background + centered source framing";

    public string OverlayText { get; } = "Your custom caption will appear here.";
}
