using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using ReelsVideoEditor.App.ViewModels;

namespace ReelsVideoEditor.App.Views;

public partial class MainWindow : Window
{
    private const int MenuTileCount = 6;
    private const double DefaultMenuTileHeight = 56;
    private const double DefaultMenuSpacing = 8;
    private const double DefaultMenuIconSize = 20;
    private const double DefaultTopOffsetIconMargin = 4;
    private const double MinMenuScale = 0.72;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        Opened += (_, _) => UpdateSidebarMenuSizing();
        SizeChanged += (_, _) => UpdateSidebarMenuSizing();
        SidebarMenuBorder.SizeChanged += (_, _) => UpdateSidebarMenuSizing();
    }

    private void UpdateSidebarMenuSizing()
    {
        var availableHeight = Math.Max(
            0,
            SidebarMenuBorder.Bounds.Height - SidebarMenuBorder.Padding.Top - SidebarMenuBorder.Padding.Bottom);

        var defaultRequiredHeight = (MenuTileCount * DefaultMenuTileHeight) + ((MenuTileCount - 1) * DefaultMenuSpacing);
        if (availableHeight <= 0 || defaultRequiredHeight <= 0)
        {
            return;
        }

        var scale = Math.Clamp(availableHeight / defaultRequiredHeight, MinMenuScale, 1.0);
        var tileHeight = Math.Round(DefaultMenuTileHeight * scale, 2);
        var tileSpacing = Math.Round(DefaultMenuSpacing * scale, 2);
        var iconSize = Math.Round(DefaultMenuIconSize * scale, 2);
        var topOffsetMargin = Math.Round(DefaultTopOffsetIconMargin * scale, 2);

        SidebarMenuStack.Spacing = tileSpacing;
        SetMenuButtonHeight(tileHeight);
        SetIconSize(MenuExplorerIcon, iconSize);
        SetIconSize(MenuEffectsIcon, iconSize);
        SetIconSize(MenuWatermarksIcon, iconSize, topOffsetMargin);
        SetIconSize(MenuTextIcon, iconSize, topOffsetMargin);
        SetIconSize(MenuSubtitlesIcon, iconSize);
        SetIconSize(MenuExportIcon, iconSize);
    }

    private void SetMenuButtonHeight(double height)
    {
        if (MenuExplorerButton is not null) MenuExplorerButton.Height = height;
        if (MenuEffectsButton is not null) MenuEffectsButton.Height = height;
        if (MenuWatermarksButton is not null) MenuWatermarksButton.Height = height;
        if (MenuTextButton is not null) MenuTextButton.Height = height;
        if (MenuSubtitlesButton is not null) MenuSubtitlesButton.Height = height;
        if (MenuExportButton is not null) MenuExportButton.Height = height;
    }

    private void SetIconSize(Path? icon, double size, double topMargin = 0)
    {
        if (icon is null)
        {
            return;
        }

        icon.Width = size;
        icon.Height = size;
        icon.Margin = new Thickness(0, topMargin, 0, 0);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Space)
        {
            return;
        }

        var focusedElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focusedElement is Button)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var command = viewModel.Preview.TogglePlayPauseCommand;
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        eventArgs.Handled = true;
    }
}

public sealed class BoolToBackgroundConverter : IValueConverter
{
    private readonly SolidColorBrush activeBrush = new(new Color(0x33, 0xC2, 0xD8, 0xC4));
    private readonly SolidColorBrush inactiveBrush = new(Colors.Transparent);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            return isActive ? activeBrush : inactiveBrush;
        }

        return inactiveBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}

public sealed class BoolToForegroundConverter : IValueConverter
{
    private readonly SolidColorBrush activeBrush = new(new Color(0xFF, 0xC2, 0xD8, 0xC4));
    private readonly SolidColorBrush inactiveBrush = new(new Color(0x99, 0xC2, 0xD8, 0xC4));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            return isActive ? activeBrush : inactiveBrush;
        }

        return inactiveBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}