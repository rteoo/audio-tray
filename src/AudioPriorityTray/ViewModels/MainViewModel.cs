using System.Diagnostics;
using AudioPriorityTray.Core;

namespace AudioPriorityTray.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AudioManager _manager;
    private bool _isSpeakerMode;
    private bool _isHeadphoneMode;
    private bool _isCustomMode;
    private bool _isEditMode;
    private double _volume;
    private string _volumeGlyph = Glyphs.Volume;
    private int _ignoredCount;

    public MainViewModel(AudioManager manager)
    {
        _manager = manager;
        Speakers = new SectionViewModel(manager, PriorityList.Speaker, "Speakers", Glyphs.Volume);
        Headphones = new SectionViewModel(manager, PriorityList.Headphone, "Headphones", Glyphs.Headphone);
        Microphones = new SectionViewModel(manager, PriorityList.Input, "Microphones", Glyphs.Microphone);
        manager.Changed += (_, _) => Sync();
        Sync();
    }

    public SectionViewModel Speakers { get; }
    public SectionViewModel Headphones { get; }
    public SectionViewModel Microphones { get; }

    public bool IsSpeakerMode { get => _isSpeakerMode; private set => Set(ref _isSpeakerMode, value); }
    public bool IsHeadphoneMode { get => _isHeadphoneMode; private set => Set(ref _isHeadphoneMode, value); }
    public bool IsCustomMode { get => _isCustomMode; private set => Set(ref _isCustomMode, value); }
    public bool IsEditMode { get => _isEditMode; private set => Set(ref _isEditMode, value); }
    public string VolumeGlyph { get => _volumeGlyph; private set => Set(ref _volumeGlyph, value); }
    public int IgnoredCount { get => _ignoredCount; private set => Set(ref _ignoredCount, value); }

    /// <summary>The "N ignored" footer button; edit mode lists ignored devices inline instead.</summary>
    public bool ShowIgnored => IgnoredCount > 0 && !IsEditMode;

    /// <summary>Default output volume, 0..100.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (Math.Abs(_volume - value) < 0.5) return;
            _manager.SetVolume((float)(value / 100));
        }
    }

    public void Sync()
    {
        var m = _manager;
        IsCustomMode = m.IsCustomMode;
        IsSpeakerMode = !m.IsCustomMode && m.CurrentMode == OutputCategory.Speaker;
        IsHeadphoneMode = !m.IsCustomMode && m.CurrentMode == OutputCategory.Headphone;
        IsEditMode = m.IsEditMode;
        IgnoredCount = m.AllHiddenDevices.Count();
        OnPropertyChanged(nameof(ShowIgnored));

        if (Set(ref _volume, Math.Round(m.Volume * 100), nameof(Volume)))
            OnPropertyChanged(nameof(VolumeText));
        VolumeGlyph = m.IsActiveOutputMuted ? Glyphs.Mute
            : m.CurrentMode == OutputCategory.Headphone ? Glyphs.Headphone
            : Glyphs.VolumeLevel(m.Volume);

        Speakers.Sync(isVisible: m.IsCustomMode || m.CurrentMode == OutputCategory.Speaker);
        Headphones.Sync(isVisible: m.IsCustomMode || m.CurrentMode == OutputCategory.Headphone);
        Microphones.Sync(isVisible: true);
    }

    public string VolumeText => $"{_volume:0}%";

    public void SelectMode(OutputCategory mode)
    {
        if (_manager.IsCustomMode) _manager.SetCustomMode(false);
        _manager.SetMode(mode);
    }

    public void EnterManualMode() => _manager.SetCustomMode(true);

    public void ToggleEditMode() => _manager.ToggleEditMode();

    /// <summary>Mouse-wheel nudge: 2% per notch, like the macOS scroll handler.</summary>
    public void NudgeVolume(int wheelDelta) => _manager.SetVolume(_manager.Volume + wheelDelta / 120f * 0.02f);

    public IReadOnlyList<MenuEntry> BuildIgnoredMenu() =>
        _manager.AllHiddenDevices.Select(device =>
        {
            var glyph = device.Flow == DeviceFlow.Input ? Glyphs.Microphone
                : _manager.Store.GetCategory(device) == OutputCategory.Headphone ? Glyphs.Headphone
                : Glyphs.Volume;
            return _manager.IsNeverUse(device)
                ? new MenuEntry(device.Name, glyph, () => _manager.SetNeverUse(device, false), Hint: "Allow use")
                : new MenuEntry(device.Name, glyph, () => _manager.UnhideDevice(device), Hint: "Stop ignoring");
        }).ToList();

    /// <summary>The tray icon's right-click menu (with "Open") and the flyout's overflow menu.</summary>
    public IReadOnlyList<MenuEntry> BuildAppMenu(Action quit, Action? openFlyout = null)
    {
        var entries = new List<MenuEntry>();
        if (openFlyout is not null) entries.AddRange([new("Open AudioTray", Glyphs.Volume, openFlyout), MenuEntry.Separator]);
        entries.AddRange(
        [
            new("Speakers", null, () => SelectMode(OutputCategory.Speaker), IsChecked: IsSpeakerMode),
            new("Headphones", null, () => SelectMode(OutputCategory.Headphone), IsChecked: IsHeadphoneMode),
            new("Manual", null, EnterManualMode, IsChecked: IsCustomMode),
            MenuEntry.Separator,
            new("Sound settings", Glyphs.Settings, () => OpenSettings("ms-settings:sound")),
            LaunchAtLogin.IsManagedByWindows
                ? new("Startup apps settings", null, () => OpenSettings("ms-settings:startupapps"))
                : new("Start with Windows", null, () => LaunchAtLogin.SetEnabled(!LaunchAtLogin.IsEnabled), IsChecked: LaunchAtLogin.IsEnabled),
            MenuEntry.Separator,
            new("Quit", Glyphs.Close, quit),
        ]);
        return entries;
    }

    private static void OpenSettings(string uri) =>
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
}
