using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ReelsVideoEditor.App.DragDrop;
using ReelsVideoEditor.App.ViewModels.VideoFiles;

namespace ReelsVideoEditor.App.Views.VideoFiles;

public partial class VideoFilesPanelView : UserControl
{
    private VideoFilesViewModel? _boundViewModel;

    public VideoFilesPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _boundViewModel = DataContext as VideoFilesViewModel;

        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateDropHintsVisibility();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(VideoFilesViewModel.HasFiles))
        {
            UpdateDropHintsVisibility();
        }
    }

    private void UpdateDropHintsVisibility()
    {
        if (this.FindControl<StackPanel>("DropHints") is { } dropHints)
        {
            dropHints.IsVisible = _boundViewModel?.HasFiles != true;
        }
    }

    private void DropZone_OnDragOver(object? sender, DragEventArgs eventArgs)
    {
        eventArgs.DragEffects = ContainsSupportedVideo(eventArgs)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private async void DropZone_OnDrop(object? sender, DragEventArgs eventArgs)
    {
        if (DataContext is not VideoFilesViewModel viewModel)
        {
            return;
        }

        await viewModel.AddDroppedFilesAsync(ResolveDroppedPaths(eventArgs));
        eventArgs.Handled = true;
    }

    private static bool ContainsSupportedVideo(DragEventArgs eventArgs)
    {
        foreach (var filePath in ResolveDroppedPaths(eventArgs))
        {
            var extension = System.IO.Path.GetExtension(filePath);
            if (IsSupportedVideoExtension(extension))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> ResolveDroppedPaths(DragEventArgs eventArgs)
    {
#pragma warning disable CS0618
        var files = eventArgs.Data.GetFiles();
#pragma warning restore CS0618
        if (files is null)
        {
            yield break;
        }

        foreach (var file in files)
        {
            if (file is IStorageFile)
            {
                var localPath = file.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(localPath))
                {
                    yield return localPath;
                    continue;
                }

                if (file.Path is { IsAbsoluteUri: true, IsFile: true })
                {
                    yield return Uri.UnescapeDataString(file.Path.LocalPath);
                }
            }
        }
    }

    private static bool IsSupportedVideoExtension(string extension)
    {
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".avi", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase);
    }

    private async void FileTile_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not Control { DataContext: VideoFileItem fileItem })
        {
            return;
        }

        if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var payload = VideoClipDragPayload.Build(fileItem.Path, fileItem.Name, fileItem.DurationSeconds);
#pragma warning disable CS0618
        var dataObject = new DataObject();
#pragma warning restore CS0618
        dataObject.Set(VideoClipDragPayload.Format, payload);

    #pragma warning disable CS0618
        await Avalonia.Input.DragDrop.DoDragDrop(eventArgs, dataObject, DragDropEffects.Copy);
    #pragma warning restore CS0618
        eventArgs.Handled = true;
    }
}
