using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReelsVideoEditor.App.ViewModels.Export;
using System.Threading.Tasks;

namespace ReelsVideoEditor.App.Views.Export;

public partial class ExportPanelView : UserControl
{
    public ExportPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is ExportViewModel vm)
        {
            vm.RequestDirectory = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel != null)
                {
                    var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = "Select Export Destination",
                        AllowMultiple = false
                    });
                    
                    if (result.Count > 0)
                    {
                        return result[0];
                    }
                }
                return null;
            };

            vm.ShowMessage = async (title, message) =>
            {
                var msgBox = new Window
                {
                    Title = title,
                    Content = new TextBlock { Text = message, Margin = new Avalonia.Thickness(20), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is Window window)
                {
                    await msgBox.ShowDialog(window);
                }
                else
                {
                    msgBox.Show();
                }
            };
        }
    }
}
