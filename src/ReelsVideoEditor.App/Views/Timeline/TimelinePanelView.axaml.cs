using Avalonia.Controls;
using Avalonia.Input;
using ReelsVideoEditor.App.DragDrop;
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

    private void TimelineTrack_OnDragOver(object? sender, DragEventArgs eventArgs)
    {
        eventArgs.DragEffects = TryGetClipPayload(eventArgs, out _, out _, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private void TimelineTrack_OnDrop(object? sender, DragEventArgs eventArgs)
    {
        if (DataContext is not TimelineViewModel viewModel || sender is not Control trackControl)
        {
            return;
        }

        if (!TryGetClipPayload(eventArgs, out var name, out var durationSeconds, out var path))
        {
            return;
        }

        var dropPositionX = eventArgs.GetPosition(trackControl).X;
        viewModel.AddClipFromExplorer(name, path, durationSeconds, dropPositionX);
        eventArgs.Handled = true;
    }

    private static bool TryGetClipPayload(DragEventArgs eventArgs, out string name, out double durationSeconds, out string path)
    {
        name = string.Empty;
        durationSeconds = 0;
        path = string.Empty;

#pragma warning disable CS0618
        var data = eventArgs.Data;
#pragma warning restore CS0618
        if (!data.Contains(VideoClipDragPayload.Format))
        {
            return false;
        }

        var rawPayload = data.Get(VideoClipDragPayload.Format);
        var parsed = VideoClipDragPayload.TryParse(rawPayload, out path, out name, out durationSeconds);
        return parsed;
    }
}
