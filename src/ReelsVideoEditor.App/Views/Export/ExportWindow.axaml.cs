using Avalonia.Controls;

namespace ReelsVideoEditor.App.Views.Export;

public partial class ExportWindow : Window
{
    public ExportWindow()
    {
        InitializeComponent();
    }

    private void OnBackButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
