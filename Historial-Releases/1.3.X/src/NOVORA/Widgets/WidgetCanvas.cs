using System.Windows;
using System.Windows.Controls;
using Size = System.Windows.Size;

namespace NOVORA.Widgets
{
    public class WidgetCanvas : Canvas
    {
        protected override Size ArrangeOverride(Size arrangeSize)
        {
            foreach (UIElement child in Children)
            {
                if (child is WidgetBase widget)
                {
                    if (widget.Width > arrangeSize.Width)
                        widget.Width = arrangeSize.Width;
                    if (widget.Height > arrangeSize.Height)
                        widget.Height = arrangeSize.Height;

                    double left = Canvas.GetLeft(widget);
                    double top = Canvas.GetTop(widget);

                    if (left + widget.Width > arrangeSize.Width)
                        Canvas.SetLeft(widget, arrangeSize.Width - widget.Width);
                    if (top + widget.Height > arrangeSize.Height)
                        Canvas.SetTop(widget, arrangeSize.Height - widget.Height);
                }
            }

            return base.ArrangeOverride(arrangeSize);
        }
    }
}