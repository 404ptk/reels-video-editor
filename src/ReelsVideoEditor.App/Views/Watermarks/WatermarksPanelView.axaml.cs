using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReelsVideoEditor.App.ViewModels.Watermarks;

namespace ReelsVideoEditor.App.Views.Watermarks;

public partial class WatermarksPanelView : UserControl
{
    public WatermarksPanelView()
    {
        InitializeComponent();
    }

    private async void BrowseImageButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not WatermarksViewModel viewModel)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select watermark image",
            FileTypeFilter =
            [
                new FilePickerFileType("Image files")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"]
                }
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        var selectedPath = files[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            viewModel.SelectImagePathCommand.Execute(selectedPath);
        }
    }
}
