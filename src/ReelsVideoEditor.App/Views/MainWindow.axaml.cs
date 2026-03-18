using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ReelsVideoEditor.App.ViewModels;

namespace ReelsVideoEditor.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Space)
        {
            return;
        }

        var focusedElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focusedElement is Button)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var command = viewModel.Preview.TogglePlayPauseCommand;
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        eventArgs.Handled = true;
    }
}