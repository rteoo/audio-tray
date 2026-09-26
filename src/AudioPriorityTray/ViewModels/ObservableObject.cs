using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioPriorityTray.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// One context-menu line; the view turns these into Fluent menu items. A non-null
/// <see cref="IsChecked"/> makes it a checkable item (mode choice, on/off setting).
/// </summary>
public sealed record MenuEntry(string Header, string? Glyph, Action? Execute, bool? IsChecked = null, string? Hint = null)
{
    public static readonly MenuEntry Separator = new("-", null, null);

    public bool IsSeparator => ReferenceEquals(this, Separator);
}
