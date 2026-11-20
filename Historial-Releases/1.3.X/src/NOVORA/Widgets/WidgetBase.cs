using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Cursors = System.Windows.Input.Cursors;
using Brushes = System.Windows.Media.Brushes;
using Thickness = System.Windows.Thickness;
using Thumb = System.Windows.Controls.Primitives.Thumb;

namespace NOVORA.Widgets
{
    public abstract class WidgetBase : UserControl
    {
        public WidgetConfig Config { get; set; }
        public bool IsWidgetInitialized { get; private set; }

        protected WidgetBase(WidgetConfig config)
        {
            Config = config;
            Width = config.Width;
            Height = config.Height;
            MinWidth = 150;
            MinHeight = 100;

            // Hacer visible el widget: fondo blanco, borde gris, esquinas redondeadas
            Background = Brushes.White;
            BorderBrush = Brushes.Gray;
            BorderThickness = new Thickness(1);
            // (Para esquinas redondeadas se necesitaría un ControlTemplate, pero con borde simple ya se ve)

            Canvas.SetLeft(this, Config.X);
            Canvas.SetTop(this, Config.Y);
        }

        public void Initialize()
        {
            if (IsWidgetInitialized) return;
            IsWidgetInitialized = true;

            // Asa de arrastre (superior izquierda)
            var dragThumb = new Thumb
            {
                Width = 20,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.SizeAll,
                Background = Brushes.LightGray
            };
            dragThumb.DragDelta += OnDragDelta;
            // Agregar el asa como contenido del widget (para que quede visible)
            // En este ejemplo simple no lo agregamos al template, pero se puede hacer con un Grid contenedor.

            // Asa de redimensionamiento (esquina inferior derecha)
            var resizeThumb = new Thumb
            {
                Width = 20,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNWSE,
                Background = Brushes.LightGray
            };
            resizeThumb.DragDelta += OnResizeDelta;

            // Crear un Grid para colocar las asas sobre el contenido
            var root = new Grid();
            root.Children.Add(resizeThumb);
            // Si el widget ya tiene contenido, se debe agregar al Grid, pero para simplificar,
            // asumimos que el contenido se asigna en el constructor del widget hijo.

            OnInitialize();
        }

        protected abstract void OnInitialize();
        public abstract void Start();
        public abstract void Stop();
        public abstract void Refresh();

        public virtual void SaveState()
        {
            Config.X = Canvas.GetLeft(this);
            Config.Y = Canvas.GetTop(this);
            Config.Width = Width;
            Config.Height = Height;
        }

        private void OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            var host = Parent as Canvas;
            if (host == null) return;

            double newLeft = Canvas.GetLeft(this) + e.HorizontalChange;
            double newTop = Canvas.GetTop(this) + e.VerticalChange;

            newLeft = Math.Max(0, Math.Min(newLeft, host.ActualWidth - ActualWidth));
            newTop = Math.Max(0, Math.Min(newTop, host.ActualHeight - ActualHeight));

            Canvas.SetLeft(this, newLeft);
            Canvas.SetTop(this, newTop);
        }

        private void OnResizeDelta(object sender, DragDeltaEventArgs e)
        {
            var host = Parent as Canvas;
            if (host == null) return;

            double newWidth = Width + e.HorizontalChange;
            double newHeight = Height + e.VerticalChange;

            // Limitar a mínimo y máximo (el del contenedor)
            newWidth = Math.Max(MinWidth, Math.Min(newWidth, host.ActualWidth - Canvas.GetLeft(this)));
            newHeight = Math.Max(MinHeight, Math.Min(newHeight, host.ActualHeight - Canvas.GetTop(this)));

            Width = newWidth;
            Height = newHeight;
        }
    }
}