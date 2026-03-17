using ReelsVideoEditor.App.ViewModels.Preview;
using ReelsVideoEditor.App.ViewModels.Timeline;
using ReelsVideoEditor.App.ViewModels.VideoFiles;

namespace ReelsVideoEditor.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public PreviewViewModel Preview { get; } = new();

    public VideoFilesViewModel VideoFiles { get; } = new();

    public TimelineViewModel Timeline { get; } = new();
}
