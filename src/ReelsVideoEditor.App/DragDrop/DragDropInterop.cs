using System.Threading.Tasks;
using Avalonia.Input;

namespace ReelsVideoEditor.App.DragDrop;

public static class DragDropInterop
{
    public static async Task StartCopyDragAsync(PointerEventArgs eventArgs, string format, string payload)
    {
#pragma warning disable CS0618
        var dataObject = new DataObject();
#pragma warning restore CS0618
        dataObject.Set(format, payload);

#pragma warning disable CS0618
        await Avalonia.Input.DragDrop.DoDragDrop(eventArgs, dataObject, DragDropEffects.Copy);
#pragma warning restore CS0618
    }
}
