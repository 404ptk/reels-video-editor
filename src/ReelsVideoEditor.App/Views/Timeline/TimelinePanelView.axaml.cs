using Avalonia.Controls;
using Avalonia.Input;
using ReelsVideoEditor.App.ViewModels.Timeline;

namespace ReelsVideoEditor.App.Views.Timeline;

public partial class TimelinePanelView : UserControl
{
    public TimelinePanelView()
    {
        InitializeComponent();
    }

    private void TimelineScrollViewer_OnPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
    {
        if (DataContext is TimelineViewModel viewModel && sender is ScrollViewer scrollViewer)
        {
            viewModel.ChangeZoomFromWheel(eventArgs.Delta.Y, scrollViewer.Bounds.Width);
            eventArgs.Handled = true;
        }
    }
}
