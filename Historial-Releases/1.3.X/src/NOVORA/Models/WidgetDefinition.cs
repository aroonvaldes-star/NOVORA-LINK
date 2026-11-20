using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NOVORA.Models;

public sealed class WidgetDefinition : INotifyPropertyChanged
{
    private bool _isActive;

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
        }
    }

    public WidgetDefinition(string id, string title, string description, bool isActive = false)
    {
        Id = id;
        Title = title;
        Description = description;
        _isActive = isActive;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}