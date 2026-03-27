using Avalonia.Controls;
using Avalonia.Input;
using ReelsVideoEditor.App.DragDrop;
using ReelsVideoEditor.App.Models;

namespace ReelsVideoEditor.App.Views.Text;

public partial class TextPanelView : UserControl
{
    public TextPanelView()
    {
        InitializeComponent();
    }

    private async void PresetTile_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not Control { DataContext: TextPresetDefinition preset })
        {
            return;
        }

        if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var payload = TextPresetDragPayload.Build(preset);
#pragma warning disable CS0618
        var dataObject = new DataObject();
#pragma warning restore CS0618
        dataObject.Set(TextPresetDragPayload.Format, payload);

#pragma warning disable CS0618
        await Avalonia.Input.DragDrop.DoDragDrop(eventArgs, dataObject, DragDropEffects.Copy);
#pragma warning restore CS0618
        eventArgs.Handled = true;
    }
}
