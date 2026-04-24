using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ReelsVideoEditor.App.Views.About;

public partial class LicenseWindow : Window
{
    public LicenseWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
        eventArgs.Handled = true;
    }
}