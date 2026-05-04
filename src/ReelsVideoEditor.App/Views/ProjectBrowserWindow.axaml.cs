using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ReelsVideoEditor.App.ViewModels;

namespace ReelsVideoEditor.App.Views;

public partial class ProjectBrowserWindow : Window
{
    public ProjectBrowserWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
