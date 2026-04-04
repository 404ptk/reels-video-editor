using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ReelsVideoEditor.App.DragDrop;
using ReelsVideoEditor.App.Models;
using ReelsVideoEditor.App.ViewModels.Text;

namespace ReelsVideoEditor.App.Views.Text;

public partial class TextPanelView : UserControl
{
    private bool isSizeDragActive;
    private double sizeDragStartX;
    private double sizeDragStartValue;
    private bool isOutlineThicknessDragActive;
    private double outlineThicknessDragStartX;
    private double outlineThicknessDragStartValue;
    private bool isLineHeightDragActive;
    private double lineHeightDragStartX;
    private double lineHeightDragStartValue;
    private bool isLetterSpacingDragActive;
    private double letterSpacingDragStartX;
    private double letterSpacingDragStartValue;

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

        if (preset.IsAddTile)
        {
            if (DataContext is TextViewModel addPresetViewModel
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
            || (eventArgs.Source is Control sourceControl && sourceControl.FindAncestorOfType<Button>() is not null))
        {
            return;
        }

        if (eventArgs.ClickCount >= 2)
        {
            if (DataContext is TextViewModel viewModel)
            {
                viewModel.BeginPresetEdit(preset);
            }

            eventArgs.Handled = true;
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

    private void SizeValue_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not InputElement dragElement)
        {
            return;
        }

        if (!eventArgs.GetCurrentPoint(dragElement).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (DataContext is not TextViewModel viewModel)
        {
            return;
        }

        isSizeDragActive = true;
        sizeDragStartX = eventArgs.GetPosition(this).X;
        sizeDragStartValue = viewModel.SelectedClipFontSize;
        eventArgs.Pointer.Capture(dragElement);
        eventArgs.Handled = true;
    }

    private void SizeValue_OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (!isSizeDragActive)
        {
            return;
        }

        if (DataContext is not TextViewModel viewModel)
        {
            return;
        }

        var deltaX = eventArgs.GetPosition(this).X - sizeDragStartX;
        var nextSize = Math.Clamp(sizeDragStartValue + (deltaX * 0.25), 10, 180);
        viewModel.SelectedClipFontSize = Math.Round(nextSize, MidpointRounding.AwayFromZero);
        eventArgs.Handled = true;
    }

    private void SizeValue_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        EndSizeDrag(eventArgs.Pointer);
        eventArgs.Handled = true;
    }

    private void SizeValue_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
    {
        EndSizeDrag(eventArgs.Pointer);
    }

    private void EndSizeDrag(IPointer? pointer)
    {
        if (!isSizeDragActive)
        {
            return;
        }

        isSizeDragActive = false;
        if (pointer is not null)
        {
            pointer.Capture(null);
        }
    }

    private void OutlineThicknessValue_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not InputElement dragElement)
        {
            return;
        }

        if (!eventArgs.GetCurrentPoint(dragElement).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (DataContext is not TextViewModel viewModel)
        {
            return;
        }

        isOutlineThicknessDragActive = true;
        outlineThicknessDragStartX = eventArgs.GetPosition(this).X;
        outlineThicknessDragStartValue = viewModel.SelectedClipOutlineThickness;
        eventArgs.Pointer.Capture(dragElement);
        eventArgs.Handled = true;
    }

    private void OutlineThicknessValue_OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (!isOutlineThicknessDragActive)
        {
            return;
        }

        if (DataContext is not TextViewModel viewModel)
        {
            return;
        }

        var deltaX = eventArgs.GetPosition(this).X - outlineThicknessDragStartX;
        var nextThickness = Math.Clamp(outlineThicknessDragStartValue + (deltaX * 0.1), 0, 24);
        viewModel.SelectedClipOutlineThickness = Math.Round(nextThickness, MidpointRounding.AwayFromZero);
        eventArgs.Handled = true;
    }

    private void OutlineThicknessValue_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        EndOutlineThicknessDrag(eventArgs.Pointer);
        eventArgs.Handled = true;
    }

    private void OutlineThicknessValue_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
    {
        EndOutlineThicknessDrag(eventArgs.Pointer);
    }

    private void EndOutlineThicknessDrag(IPointer? pointer)
    {
        if (!isOutlineThicknessDragActive)
        {
            return;
        }

        isOutlineThicknessDragActive = false;
        if (pointer is not null)
        {
            pointer.Capture(null);
        }
    }

    private void LineHeightValue_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not InputElement dragElement)
        {
            return;
        }

        if (!eventArgs.GetCurrentPoint(dragElement).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (DataContext is not TextViewModel viewModel)
        {
            return;
        }

        isLineHeightDragActive = true;
        lineHeightDragStartX = eventArgs.GetPosition(this).X;
        lineHeightDragStartValue = viewModel.SelectedClipLineHeightMultiplier;
        eventArgs.Pointer.Capture(dragElement);
        eventArgs.Handled = true;
    }

    private void LineHeightValue_OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (!isLineHeightDragActive)
        {
            return;
        }

        if (DataContext is not TextViewModel viewModel)
        {
            return;
        }

        var deltaX = eventArgs.GetPosition(this).X - lineHeightDragStartX;
        var nextValue = Math.Clamp(lineHeightDragStartValue + (deltaX * 0.005), 0.7, 2.5);
        viewModel.SelectedClipLineHeightMultiplier = Math.Round(nextValue, 2, MidpointRounding.AwayFromZero);
        eventArgs.Handled = true;
    }

    private void LineHeightValue_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        EndLineHeightDrag(eventArgs.Pointer);
        eventArgs.Handled = true;
    }

    private void LineHeightValue_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
    {
        EndLineHeightDrag(eventArgs.Pointer);
    }

    private void EndLineHeightDrag(IPointer? pointer)
    {
        if (!isLineHeightDragActive)
        {
            return;
        }

        isLineHeightDragActive = false;
        if (pointer is not null)
        {
            pointer.Capture(null);
        }
    }

    private void LetterSpacingValue_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not InputElement dragElement)
        {
            return;
        }

        if (!eventArgs.GetCurrentPoint(dragElement).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (DataContext is not TextViewModel viewModel)
        {
            return;
        }

        isLetterSpacingDragActive = true;
        letterSpacingDragStartX = eventArgs.GetPosition(this).X;
        letterSpacingDragStartValue = viewModel.SelectedClipLetterSpacing;
        eventArgs.Pointer.Capture(dragElement);
        eventArgs.Handled = true;
    }

    private void LetterSpacingValue_OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (!isLetterSpacingDragActive)
        {
            return;
        }

        if (DataContext is not TextViewModel viewModel)
        {
            return;
        }

        var deltaX = eventArgs.GetPosition(this).X - letterSpacingDragStartX;
        var nextValue = Math.Clamp(letterSpacingDragStartValue + (deltaX * 0.05), 0, 20);
        viewModel.SelectedClipLetterSpacing = Math.Round(nextValue, 1, MidpointRounding.AwayFromZero);
        eventArgs.Handled = true;
    }

    private void LetterSpacingValue_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        EndLetterSpacingDrag(eventArgs.Pointer);
        eventArgs.Handled = true;
    }

    private void LetterSpacingValue_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
    {
        EndLetterSpacingDrag(eventArgs.Pointer);
    }

    private void EndLetterSpacingDrag(IPointer? pointer)
    {
        if (!isLetterSpacingDragActive)
        {
            return;
        }

        isLetterSpacingDragActive = false;
        if (pointer is not null)
        {
            pointer.Capture(null);
        }
    }
}
