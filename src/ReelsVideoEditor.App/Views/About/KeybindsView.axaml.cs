using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ReelsVideoEditor.App.Views.About;

public partial class KeybindsView : UserControl
{
    public KeybindsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
