using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ReelsVideoEditor.App.Views.Subtitles;

public partial class SelectWhisperModelWindow : Window
{
    public string? SelectedModel { get; private set; }

    public SelectWhisperModelWindow()
    {
        InitializeComponent();
    }

    private void ConfirmButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        var comboBox = this.FindControl<ComboBox>("ModelComboBox");
        if (comboBox?.SelectedItem is ComboBoxItem item && item.Tag is string model)
        {
            SelectedModel = model;
            Close(SelectedModel);
        }
        else
        {
            SelectedModel = "base";
            Close("base");
        }
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        SelectedModel = null;
        Close(null);
    }
}
