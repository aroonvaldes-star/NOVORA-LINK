using System.Windows;
using System.Windows.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace NOVORA.Widgets
{
    public sealed class RedWidget : WidgetBase
    {
        private readonly TextBlock _statusText;

        public RedWidget(WidgetConfig config) : base(config)
        {
            var panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(new TextBlock
            {
                Text = "RED",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            _statusText = new TextBlock
            {
                Text = "Sin dispositivo conectado.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_statusText);

            Content = panel;
        }

        protected override void OnInitialize() { }
        public override void Start() { }
        public override void Stop() { }
        public override void Refresh() { }
    }
}