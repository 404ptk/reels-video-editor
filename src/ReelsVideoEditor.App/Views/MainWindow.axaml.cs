using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

public sealed class BoolToBackgroundConverter : IValueConverter
{
    private readonly SolidColorBrush activeBrush = new(new Color(0x33, 0xC2, 0xD8, 0xC4));
    private readonly SolidColorBrush inactiveBrush = new(Colors.Transparent);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            return isActive ? activeBrush : inactiveBrush;
        }

        return inactiveBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}

public sealed class BoolToForegroundConverter : IValueConverter
{
    private readonly SolidColorBrush activeBrush = new(new Color(0xFF, 0xC2, 0xD8, 0xC4));
    private readonly SolidColorBrush inactiveBrush = new(new Color(0x99, 0xC2, 0xD8, 0xC4));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            return isActive ? activeBrush : inactiveBrush;
        }

        return inactiveBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}