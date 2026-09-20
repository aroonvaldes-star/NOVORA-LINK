using System.Windows;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private Window CreateNovoraDialog(string title, double width, UIElement content, Thickness? padding = null)
    {
        var host = new System.Windows.Controls.Border
        {
            Padding = padding ?? new Thickness(22),
            Child = content
        };
        host.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "WindowBrush");

        var dialog = new Window
        {
            Owner = this,
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = host
        };
        dialog.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        return dialog;
    }

    private void ApplyNovoraText(System.Windows.Controls.TextBlock textBlock, string brushKey = "TextBrush")
    {
        textBlock.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, brushKey);
    }

    private void ApplyNovoraInput(System.Windows.Controls.ListBox listBox)
    {
        listBox.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "InputBackgroundBrush");
        listBox.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "InputForegroundBrush");
        listBox.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "InputBorderBrush");
    }

    private void ApplyNovoraActionButton(System.Windows.Controls.Button button)
    {
        button.SetResourceReference(FrameworkElement.StyleProperty, "ActionButton");
    }
}
