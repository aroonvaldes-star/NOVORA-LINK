using NOVORA.Models;

namespace NOVORA.Widgets
{
    public sealed class WidgetRegistry
    {
        private readonly Dictionary<string, Func<WidgetConfig, WidgetBase>> _factories = new();
        private readonly Dictionary<string, WidgetDefinition> _definitions = new();

        public IReadOnlyList<WidgetDefinition> All => _definitions.Values.ToList();

        public WidgetRegistry()
        {
            // Registrar los tipos disponibles (añade más aquí)
            Register("red", "RED", "Red, ADB y Gnirehtet.", config => new RedWidget(config));
            Register("transmission", "Transmisión", "Control de Scrcpy.", config => new TransmissionWidget(config));
            Register("console", "Consola", "Comandos NOVORA, ADB, scrcpy y Gnirehtet.", config => new ConsoleWidget(config));
            Register("performance", "Rendimiento", "CPU, memoria y batería.", config => new PerformanceWidget(config));
        }

        public void Register(string type, string title, string description, Func<WidgetConfig, WidgetBase> factory)
        {
            _factories[type] = factory;
            _definitions[type] = new WidgetDefinition(type, title, description, false);
        }

        public WidgetBase CreateWidget(string type, WidgetConfig config)
        {
            if (_factories.TryGetValue(type, out var factory))
                return factory(config);
            throw new NotSupportedException($"Widget type '{type}' not registered.");
        }

        public IEnumerable<string> GetAvailableTypes() => _factories.Keys;
    }
}