using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ReelsVideoEditor.App.Views.Common;

public partial class ActionButton : UserControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<ActionButton, ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<ActionButton, object?>(nameof(CommandParameter));

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ActionButton, string>(nameof(Label), "Action");

    public static readonly StyledProperty<double> ButtonWidthProperty =
        AvaloniaProperty.Register<ActionButton, double>(nameof(ButtonWidth), 120);

    public static readonly StyledProperty<HorizontalAlignment> LabelHorizontalAlignmentProperty =
        AvaloniaProperty.Register<ActionButton, HorizontalAlignment>(nameof(LabelHorizontalAlignment), HorizontalAlignment.Center);

    public ActionButton()
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

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public double ButtonWidth
    {
        get => GetValue(ButtonWidthProperty);
        set => SetValue(ButtonWidthProperty, value);
    }

    public HorizontalAlignment LabelHorizontalAlignment
    {
        get => GetValue(LabelHorizontalAlignmentProperty);
        set => SetValue(LabelHorizontalAlignmentProperty, value);
    }
}