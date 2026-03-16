using ReelsVideoEditor.App.ViewModels.Preview;
using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public PreviewViewModel Preview { get; } = new();

    public TimelineViewModel Timeline { get; } = new();
}
