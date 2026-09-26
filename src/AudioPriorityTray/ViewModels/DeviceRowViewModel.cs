using AudioPriorityTray.Core;

namespace AudioPriorityTray.ViewModels;

public sealed class DeviceRowViewModel(AudioDevice device) : ObservableObject
{
    private AudioDevice _device = device;
    private string _name = device.Name;
    private string _priorityText = "";
    private bool _isActive;
    private bool _isMuted;
    private bool _isNeverUse;
    private bool _isDisconnected;
    private string _statusGlyph = "";
    private string _statusText = "";
    private bool _isDragging;

    public AudioDevice Device { get => _device; private set => Set(ref _device, value); }
    public string Name { get => _name; private set => Set(ref _name, value); }
    public string PriorityText { get => _priorityText; private set => Set(ref _priorityText, value); }
    public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }
    public bool IsMuted { get => _isMuted; private set => Set(ref _isMuted, value); }
    public bool IsNeverUse { get => _isNeverUse; private set => Set(ref _isNeverUse, value); }
    public bool IsDisconnected { get => _isDisconnected; private set => Set(ref _isDisconnected, value); }

    /// <summary>Disconnected, ignored (edit mode) or never-use marker; empty when none applies.</summary>
    public string StatusGlyph { get => _statusGlyph; private set => Set(ref _statusGlyph, value); }

    /// <summary>"Last seen" for disconnected devices.</summary>
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public bool IsDragging { get => _isDragging; set => Set(ref _isDragging, value); }

    /// <summary>What the row shows visually, as text for screen readers.</summary>
    public string AccessibleStatus
    {
        get
        {
            var parts = new List<string> { $"Priority {PriorityText}" };
            if (IsActive) parts.Add("Active");
            if (IsMuted) parts.Add("Muted");
            if (IsDisconnected) parts.Add(StatusText.Length > 0 ? $"Disconnected, last seen {StatusText}" : "Disconnected");
            else if (IsNeverUse) parts.Add("Never use");
            else if (StatusGlyph == Glyphs.Hide) parts.Add("Ignored");
            return string.Join(", ", parts);
        }
    }

    public void Update(AudioManager manager, AudioDevice device, int index, string? currentId, PriorityList list)
    {
        Device = device;
        Name = device.Name;
        PriorityText = (index + 1).ToString();
        IsDisconnected = !device.IsConnected;
        IsActive = device.IsConnected && device.Id == currentId;
        IsMuted = device.IsConnected && manager.IsDeviceMuted(device);
        IsNeverUse = manager.IsNeverUse(device);

        StatusGlyph = IsDisconnected ? Glyphs.Disconnected
            : manager.IsEditMode && manager.IsDeviceIgnored(device, list) ? Glyphs.Hide
            : IsNeverUse ? Glyphs.Blocked
            : "";
        StatusText = IsDisconnected && manager.Store.GetStoredDevice(device.Id) is { } stored
            ? stored.LastSeenRelative(manager.Now)
            : "";
        OnPropertyChanged(nameof(AccessibleStatus));
    }
}
