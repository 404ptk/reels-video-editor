using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReelsVideoEditor.App.ViewModels.Watermarks;

namespace ReelsVideoEditor.App.Views.Watermarks;

public partial class WatermarksPanelView : UserControl
{
    private bool isOpacityDragActive;
    private double opacityDragStartX;
    private double opacityDragStartValue;

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

    private void LoadImageActionButton_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BrowseImageButton_OnClick(sender, new RoutedEventArgs());
        eventArgs.Handled = true;
    }

    private async void PresetTile_OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs eventArgs)
    {
        if (sender is not Control { DataContext: ReelsVideoEditor.App.Models.WatermarkPresetDefinition preset })
        {
            return;
        }

        if (preset.IsAddTile)
        {
            if (DataContext is WatermarksViewModel addPresetViewModel
                && addPresetViewModel.CreatePresetFromTileCommand.CanExecute(null))
            {
                addPresetViewModel.CreatePresetFromTileCommand.Execute(null);
            }

            eventArgs.Handled = true;
            return;
        }

        if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (eventArgs.Source is Button
            || (eventArgs.Source is Control sourceControl && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(sourceControl) is not null))
        {
            return;
        }

        if (eventArgs.ClickCount >= 2)
        {
            if (DataContext is WatermarksViewModel viewModel)
            {
                viewModel.BeginPresetEdit(preset);
            }

            eventArgs.Handled = true;
            return;
        }

        var payload = ReelsVideoEditor.App.DragDrop.WatermarkPresetDragPayload.Build(preset);
#pragma warning disable CS0618
        var dataObject = new Avalonia.Input.DataObject();
#pragma warning restore CS0618
        dataObject.Set(ReelsVideoEditor.App.DragDrop.WatermarkPresetDragPayload.Format, payload);

#pragma warning disable CS0618
        await Avalonia.Input.DragDrop.DoDragDrop(eventArgs, dataObject, Avalonia.Input.DragDropEffects.Copy);
#pragma warning restore CS0618
        eventArgs.Handled = true;
    }

    private void PresetTile_OnPointerEntered(object? sender, Avalonia.Input.PointerEventArgs eventArgs)
    {
    }

    private void OpacityValue_OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs eventArgs)
    {
        if (sender is not InputElement dragElement)
        {
            return;
        }

        if (!eventArgs.GetCurrentPoint(dragElement).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (DataContext is not WatermarksViewModel viewModel)
        {
            return;
        }

        isOpacityDragActive = true;
        opacityDragStartX = eventArgs.GetPosition(this).X;
        opacityDragStartValue = viewModel.SelectedOpacity;
        eventArgs.Pointer.Capture(dragElement);
        eventArgs.Handled = true;
    }

    private void OpacityValue_OnPointerMoved(object? sender, Avalonia.Input.PointerEventArgs eventArgs)
    {
        if (!isOpacityDragActive)
        {
            return;
        }

        if (DataContext is not WatermarksViewModel viewModel)
        {
            return;
        }

        var deltaX = eventArgs.GetPosition(this).X - opacityDragStartX;
        var nextOpacity = Math.Clamp(opacityDragStartValue + (deltaX * 0.003), 0.0, 1.0);
        viewModel.SelectedOpacity = Math.Round(nextOpacity, 2, MidpointRounding.AwayFromZero);
        eventArgs.Handled = true;
    }

    private void OpacityValue_OnPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs eventArgs)
    {
        EndOpacityDrag(eventArgs.Pointer);
        eventArgs.Handled = true;
    }

    private void OpacityValue_OnPointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs eventArgs)
    {
        EndOpacityDrag(eventArgs.Pointer);
    }

    private void EndOpacityDrag(Avalonia.Input.IPointer? pointer)
    {
        if (!isOpacityDragActive)
        {
            return;
        }

        isOpacityDragActive = false;
        pointer?.Capture(null);
    }
}
