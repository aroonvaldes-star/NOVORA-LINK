namespace NOVORA.Models;

public sealed class WidgetConfig
{
    public string WidgetType { get; set; } = string.Empty;
    public string WidgetId { get; set; } = Guid.NewGuid().ToString("N");
    public double X { get; set; } = 10;
    public double Y { get; set; } = 10;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 200;
    public int ZIndex { get; set; }
    public bool IsVisible { get; set; } = true;
    public Dictionary<string, object> Settings { get; set; } = new();
}
