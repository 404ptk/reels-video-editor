using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
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
    private TopLevel? hostTopLevel;

    public TextPanelView()
    {
        InitializeComponent();
        Focusable = true;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        hostTopLevel = TopLevel.GetTopLevel(this);
        hostTopLevel?.AddHandler(InputElement.PointerPressedEvent, OnHostPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        if (hostTopLevel is not null)
        {
            hostTopLevel.RemoveHandler(InputElement.PointerPressedEvent, OnHostPointerPressed);
            hostTopLevel = null;
        }
    }

    private void OnHostPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (DataContext is not TextViewModel viewModel || !viewModel.IsFontDropdownOpen)
        {
            return;
        }

        if (eventArgs.Source is not Visual sourceVisual)
        {
            return;
        }

        if ((FontPickerRoot is not null && IsInsideVisual(sourceVisual, FontPickerRoot))
            || (FontPickerPopupContent is not null && IsInsideVisual(sourceVisual, FontPickerPopupContent)))
        {
            return;
        }

        viewModel.IsFontDropdownOpen = false;
        if (FontSearchTextBox?.IsFocused == true)
        {
            Focus();
        }
    }

    private static bool IsInsideVisual(Visual source, Visual container)
    {
        for (var current = source; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, container))
            {
                return true;
            }
        }

        return false;
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

    private void PresetTile_OnPointerEntered(object? sender, PointerEventArgs eventArgs)
    {
        if (sender is not Control { DataContext: TextPresetDefinition preset })
        {
            return;
        }

        if (preset.IsAddTile)
        {
            return;
        }

        var normalizedEffect = Models.TextRevealEffect.Normalize(preset.TextRevealEffect);
        if (string.Equals(normalizedEffect, Models.TextRevealEffect.None, StringComparison.Ordinal))
        {
            return;
        }

        if (sender is not Visual senderVisual)
        {
            return;
        }

        var previewImage = senderVisual
            .GetVisualDescendants()
            .OfType<Avalonia.Controls.Image>()
            .FirstOrDefault();

        if (previewImage is null)
        {
            return;
        }

        RunPopAnimation(previewImage);
    }

    private static void RunPopAnimation(Avalonia.Controls.Image target)
    {
        const double peakScaleBoost = 0.14;
        const double animationDurationMs = 220.0;
        const double frameIntervalMs = 16.0;

        var scaleTransform = target.RenderTransform as ScaleTransform;
        if (scaleTransform is null)
        {
            scaleTransform = new ScaleTransform(1.0, 1.0);
            target.RenderTransform = scaleTransform;
        }

        scaleTransform.ScaleX = 1.0 + peakScaleBoost;
        scaleTransform.ScaleY = 1.0 + peakScaleBoost;

        var startTime = Environment.TickCount64;

        DispatcherTimer? timer = null;
        timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameIntervalMs)
        };

        timer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startTime;
            if (elapsed >= animationDurationMs)
            {
                scaleTransform.ScaleX = 1.0;
                scaleTransform.ScaleY = 1.0;
                timer.Stop();
                return;
            }

            var t = elapsed / animationDurationMs;
            var eased = 1.0 - Math.Pow(1.0 - t, 3);
            var scale = 1.0 + (peakScaleBoost * (1.0 - eased));
            scaleTransform.ScaleX = scale;
            scaleTransform.ScaleY = scale;
        };

        timer.Start();
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

    private void FontSearchTextBox_OnGotFocus(object? sender, GotFocusEventArgs eventArgs)
    {
        if (DataContext is TextViewModel viewModel && viewModel.IsEditorVisible)
        {
            viewModel.IsFontDropdownOpen = true;
        }
    }

    private void FontSearchTextBox_OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (DataContext is not TextViewModel viewModel)
        {
            return;
        }

        if (eventArgs.Key == Key.Down)
        {
            viewModel.IsFontDropdownOpen = true;
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.Escape)
        {
            viewModel.IsFontDropdownOpen = false;
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.Enter)
        {
            var firstMatch = viewModel.FilteredAvailableFonts.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstMatch))
            {
                viewModel.SelectFilteredFontCommand.Execute(firstMatch);
                if (sender is TextBox textBox)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        textBox.Focus();
                        var caret = (textBox.Text ?? string.Empty).Length;
                        textBox.CaretIndex = caret;
                        textBox.SelectionStart = caret;
                        textBox.SelectionEnd = caret;
                    }, DispatcherPriority.Input);
                }

                eventArgs.Handled = true;
            }
        }
    }
}
