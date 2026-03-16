using Avalonia.Controls;
using Avalonia.Input;
using ReelsVideoEditor.App.ViewModels;

namespace ReelsVideoEditor.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void TimelineScrollViewer_OnPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ChangeZoomFromWheel(eventArgs.Delta.Y);
            eventArgs.Handled = true;
        }
    }
}