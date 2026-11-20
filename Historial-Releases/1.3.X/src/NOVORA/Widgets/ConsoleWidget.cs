using System.Windows;
using System.Windows.Controls;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;

namespace NOVORA.Widgets
{
    public sealed class ConsoleWidget : WidgetBase
    {
        private readonly TextBox _consoleOutput;
        private readonly TextBox _consoleInput;

        public ConsoleWidget(WidgetConfig config) : base(config)
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(110) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _consoleOutput = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11
            };
            Grid.SetRow(_consoleOutput, 0);
            panel.Children.Add(_consoleOutput);

            var inputRow = new Grid();
            inputRow.ColumnDefinitions.Add(new ColumnDefinition());
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _consoleInput = new TextBox
            {
                Height = 32,
                Padding = new Thickness(7)
            };
            Grid.SetColumn(_consoleInput, 0);
            inputRow.Children.Add(_consoleInput);

            var runButton = new Button
            {
                Content = "EJECUTAR",
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(3, 0, 0, 0),
                MinHeight = 30
            };
            Grid.SetColumn(runButton, 1);
            inputRow.Children.Add(runButton);

            Grid.SetRow(inputRow, 1);
            panel.Children.Add(inputRow);

            Content = panel;
        }

        protected override void OnInitialize() { }
        public override void Start() { }
        public override void Stop() { }
        public override void Refresh() { }
    }
}