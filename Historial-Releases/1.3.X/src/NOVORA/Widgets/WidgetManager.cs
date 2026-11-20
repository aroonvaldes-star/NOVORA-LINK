using System.Collections.ObjectModel;
using System.Windows.Controls;
using NOVORA.Services;

namespace NOVORA.Widgets;

public class WidgetManager
{
    private readonly Canvas _canvas;
    private readonly WidgetRegistry _registry;
    private readonly SettingsService _settingsService;

    public ObservableCollection<WidgetBase> Widgets { get; } = new();

    public WidgetManager(
        Canvas canvas,
        WidgetRegistry registry,
        SettingsService settingsService)
    {
        _canvas = canvas
            ?? throw new ArgumentNullException(nameof(canvas));

        _registry = registry
            ?? throw new ArgumentNullException(nameof(registry));

        _settingsService = settingsService
            ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public void LoadWidgets()
    {
        var settings = _settingsService.Load();

        foreach (var config in settings.Widgets)
        {
            try
            {
                var widget =
                    _registry.CreateWidget(
                        config.WidgetType,
                        config);

                widget.Initialize();

                _canvas.Children.Add(widget);
                Widgets.Add(widget);

                widget.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Error al cargar widget {config.WidgetType}: {ex.Message}");
            }
        }
    }

    public void AddWidget(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return;

        var config = new WidgetConfig
        {
            WidgetType = type
        };

        var widget =
            _registry.CreateWidget(
                type,
                config);

        widget.Initialize();

        _canvas.Children.Add(widget);
        Widgets.Add(widget);

        widget.Start();
    }

    public void RemoveWidget(WidgetBase widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        widget.Stop();

        _canvas.Children.Remove(widget);
        Widgets.Remove(widget);

        SaveAll();
    }

    public void SaveAll()
    {
        var settings = _settingsService.Load();

        settings.Widgets =
            Widgets
                .Select(w => w.Config)
                .ToList();

        _settingsService.Save(settings);
    }
}
