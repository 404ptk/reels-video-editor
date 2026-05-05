using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ReelsVideoEditor.App.Views.Common;

public partial class AlertDialogWindow : Window
{
    public AlertDialogWindow()
    {
        InitializeComponent();
    }

    public AlertDialogWindow(string title, object content, bool showCancel = true, string confirmText = "Confirm") : this()
    {
        var titleText = this.FindControl<TextBlock>("TitleText");
        if (titleText != null)
        {
            titleText.Text = title;
            this.Title = title;
        }

        var dialogContent = this.FindControl<ContentPresenter>("DialogContent");
        if (dialogContent != null)
        {
            if (content is string strContent)
            {
                dialogContent.Content = new TextBlock 
                { 
                    Text = strContent, 
                    TextWrapping = TextWrapping.Wrap, 
                    Foreground = new SolidColorBrush(Color.Parse("#C2D8C4"))
                };
            }
            else
            {
                dialogContent.Content = content;
            }
        }

        var cancelButton = this.FindControl<Button>("CancelButton");
        if (cancelButton != null)
        {
            cancelButton.IsVisible = showCancel;
        }

        var confirmButton = this.FindControl<Button>("ConfirmButton");
        if (confirmButton != null)
        {
            confirmButton.Content = confirmText;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ConfirmButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
