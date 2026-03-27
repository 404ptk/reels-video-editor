using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ReelsVideoEditor.App.Views.Common;

public partial class BackButton : UserControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<BackButton, ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<BackButton, object?>(nameof(CommandParameter));

    public BackButton()
    {
        InitializeComponent();
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public event EventHandler<RoutedEventArgs> Click
    {
        add => AddHandler(Button.ClickEvent, value);
        remove => RemoveHandler(Button.ClickEvent, value);
    }
}