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
    private VideoFileItem? _dragCandidate;
    private Point _dragStartPoint;
    private IPointer? _dragPointer;
    private bool _isDragOperationStarted;

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
        eventArgs.DragEffects = ContainsSupportedMedia(eventArgs)
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

    private static bool ContainsSupportedMedia(DragEventArgs eventArgs)
    {
        foreach (var filePath in ResolveDroppedPaths(eventArgs))
        {
            var extension = System.IO.Path.GetExtension(filePath);
            if (IsSupportedVideoExtension(extension) || VideoFilesViewModel.IsSupportedImageExtension(extension))
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

    private void FileTile_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not Control { DataContext: VideoFileItem fileItem } tile)
        {
            return;
        }

        if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();

        if (DataContext is VideoFilesViewModel viewModel)
        {
            var keyModifiers = eventArgs.KeyModifiers;
            var extendSelection = keyModifiers.HasFlag(KeyModifiers.Shift);
            var toggleSelection = keyModifiers.HasFlag(KeyModifiers.Control);
            viewModel.SelectFile(fileItem, extendSelection, toggleSelection);
        }

        _dragCandidate = fileItem;
        _dragStartPoint = eventArgs.GetPosition(this);
        _dragPointer = eventArgs.Pointer;
        _isDragOperationStarted = false;
        _dragPointer.Capture(tile);
        eventArgs.Handled = true;
    }

    private async void FileTile_OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (_dragCandidate is null || _isDragOperationStarted || _dragPointer is null)
        {
            return;
        }

        if (!ReferenceEquals(eventArgs.Pointer, _dragPointer))
        {
            return;
        }

        var point = eventArgs.GetPosition(this);
        var deltaX = Math.Abs(point.X - _dragStartPoint.X);
        var deltaY = Math.Abs(point.Y - _dragStartPoint.Y);
        const double dragThreshold = 5;
        if (deltaX < dragThreshold && deltaY < dragThreshold)
        {
            return;
        }

        _isDragOperationStarted = true;

        var payload = VideoClipDragPayload.Build(_dragCandidate.Path, _dragCandidate.Name, _dragCandidate.DurationSeconds);
        await DragDropInterop.StartCopyDragAsync(eventArgs, VideoClipDragPayload.Format, payload);
        ResetDragState();
        eventArgs.Handled = true;
    }

    private void FileTile_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        ResetDragState();
    }

    private void FileTile_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
    {
        ResetDragState();
    }

    private void VideoFilesPanelView_OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Delete || DataContext is not VideoFilesViewModel viewModel)
        {
            return;
        }

        if (viewModel.DeleteSelectedFiles())
        {
            eventArgs.Handled = true;
        }
    }

    private void ResetDragState()
    {
        if (_dragPointer is not null)
        {
            _dragPointer.Capture(null);
        }

        _dragCandidate = null;
        _dragPointer = null;
        _isDragOperationStarted = false;
    }
}
