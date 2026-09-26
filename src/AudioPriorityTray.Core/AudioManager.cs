namespace AudioPriorityTray.Core;

/// <summary>
/// Owns the device lists and the auto-switching policy: in automatic mode the highest-priority
/// connected device of the current category is always the system default. Single-threaded;
/// the audio service raises its events on the owning thread.
/// </summary>
public sealed class AudioManager : IDisposable
{
    private readonly IAudioDeviceService _service;
    private readonly TimeProvider _time;
    private HashSet<string> _connectedIds = [];
    private HashSet<string> _previousConnectedIds = [];

    public AudioManager(IAudioDeviceService service, SettingsStore store, TimeProvider? time = null)
    {
        _service = service;
        Store = store;
        _time = time ?? TimeProvider.System;
        CurrentMode = store.CurrentMode;
        IsCustomMode = store.IsCustomMode;

        RefreshDevices();
        _previousConnectedIds = [.. _connectedIds];
        if (store.IsFirstRun) AdoptModeOfCurrentOutput();
        _service.DevicesChanged += OnDevicesChanged;
        _service.MuteOrVolumeChanged += OnMuteOrVolumeChanged;
        _service.StartListening();

        if (!IsCustomMode)
        {
            ApplyHighestPriority(PriorityList.Input);
            ApplyHighestPriorityOutput();
        }
        RefreshVolumeAndMute();
    }

    /// <summary>Raised after any state change, on the owning thread.</summary>
    public event EventHandler? Changed;

    public SettingsStore Store { get; }

    public IReadOnlyList<AudioDevice> InputDevices { get; private set; } = [];
    public IReadOnlyList<AudioDevice> SpeakerDevices { get; private set; } = [];
    public IReadOnlyList<AudioDevice> HeadphoneDevices { get; private set; } = [];
    public IReadOnlyList<AudioDevice> HiddenInputDevices { get; private set; } = [];
    public IReadOnlyList<AudioDevice> HiddenSpeakerDevices { get; private set; } = [];
    public IReadOnlyList<AudioDevice> HiddenHeadphoneDevices { get; private set; } = [];

    public string? CurrentInputId { get; private set; }
    public string? CurrentOutputId { get; private set; }
    public OutputCategory CurrentMode { get; private set; }
    public bool IsCustomMode { get; private set; }
    public bool IsEditMode { get; private set; }
    public float Volume { get; private set; }
    public IReadOnlySet<string> MutedDeviceIds { get; private set; } = new HashSet<string>();
    public bool IsActiveOutputMuted { get; private set; }
    public DateTimeOffset Now => _time.GetUtcNow();

    public IReadOnlyList<AudioDevice> Devices(PriorityList list) => list switch
    {
        PriorityList.Input => InputDevices,
        PriorityList.Speaker => SpeakerDevices,
        _ => HeadphoneDevices,
    };

    public IEnumerable<AudioDevice> AllHiddenDevices =>
        HiddenInputDevices.Concat(HiddenSpeakerDevices).Concat(HiddenHeadphoneDevices);

    public string? CurrentOutputName =>
        CurrentOutputId is null ? null : Store.GetStoredDevice(CurrentOutputId)?.Name;

    public bool IsDeviceMuted(AudioDevice device) => MutedDeviceIds.Contains(device.Id);

    public bool IsNeverUse(AudioDevice device) => Store.IsNeverUse(device);

    public bool IsDeviceIgnored(AudioDevice device, PriorityList list) => Store.IsHidden(device, list);

    // MARK: Modes

    public void SetMode(OutputCategory mode)
    {
        CurrentMode = mode;
        Store.CurrentMode = mode;
        if (!IsCustomMode) ApplyHighestPriorityOutput();
        RaiseChanged();
    }

    public void SetCustomMode(bool enabled)
    {
        IsCustomMode = enabled;
        Store.IsCustomMode = enabled;
        if (!enabled)
        {
            ApplyHighestPriority(PriorityList.Input);
            ApplyHighestPriorityOutput();
        }
        RaiseChanged();
    }

    public void ToggleEditMode()
    {
        IsEditMode = !IsEditMode;
        RefreshDevices();
        RefreshVolumeAndMute();
        RaiseChanged();
    }

    // MARK: Device actions

    /// <summary>Makes <paramref name="device"/> the system default directly (manual mode).</summary>
    public void SelectDevice(AudioDevice device)
    {
        if (!device.IsConnected) return;
        Apply(device);
        RefreshVolumeAndMute();
        RaiseChanged();
    }

    /// <summary>
    /// Moves the device at <paramref name="from"/> so it lands before the item currently at
    /// <paramref name="to"/> (<c>to == count</c> means the end), then applies the new top device
    /// when the list is the one currently in use.
    /// </summary>
    public void MoveDevice(PriorityList list, int from, int to)
    {
        var devices = Devices(list).ToList();
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, devices.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(to);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(to, devices.Count);

        var item = devices[from];
        devices.RemoveAt(from);
        devices.Insert(to > from ? to - 1 : to, item);
        SetDevices(list, devices);
        Store.SavePriorities(devices, list);

        if (list == PriorityList.Input || list.ToCategory() == CurrentMode)
        {
            ApplyHighestPriority(list);
            RefreshVolumeAndMute();
        }
        RaiseChanged();
    }

    public void SetCategory(OutputCategory category, AudioDevice device)
    {
        Store.SetCategory(category, device);
        RefreshDevices();
        if (!IsCustomMode) ApplyHighestPriorityOutput();
        RaiseChanged();
    }

    /// <summary>Ignores the device in one list (its own list when <paramref name="list"/> is null).</summary>
    public void HideDevice(AudioDevice device, PriorityList? list = null)
    {
        var target = list ?? Store.ListFor(device);
        Store.HideDevice(device, target);
        RefreshAndReapply(target);
    }

    /// <summary>Ignores an output device as both a speaker and a headphone.</summary>
    public void HideDeviceEntirely(AudioDevice device)
    {
        Store.HideDevice(device, PriorityList.Speaker);
        Store.HideDevice(device, PriorityList.Headphone);
        RefreshAndReapply(PriorityList.Speaker);
    }

    public void UnhideDevice(AudioDevice device, PriorityList? list = null)
    {
        var target = list ?? Store.ListFor(device);
        Store.UnhideDevice(device, target);
        RefreshAndReapply(target);
    }

    public void SetNeverUse(AudioDevice device, bool neverUse)
    {
        Store.SetNeverUse(device, neverUse);
        RefreshAndReapply(Store.ListFor(device));
    }

    public void ForgetDevice(AudioDevice device)
    {
        Store.ForgetDevice(device.Id);
        RefreshDevices();
        RaiseChanged();
    }

    public void SetVolume(float volume)
    {
        Volume = Math.Clamp(volume, 0f, 1f);
        _service.SetOutputVolume(Volume);
        RaiseChanged();
    }

    // MARK: Refresh

    public void RefreshDevices()
    {
        var connected = _service.GetDevices();
        _connectedIds = connected.Select(d => d.Id).ToHashSet();
        Store.RememberDevices(connected, _time.GetUtcNow());

        var inputs = connected.Where(d => d.Flow == DeviceFlow.Input).ToList();
        var outputs = connected.Where(d => d.Flow == DeviceFlow.Output).ToList();

        if (IsEditMode)
        {
            // Edit mode shows every device ever seen, ignored ones included, so they can be ordered.
            foreach (var stored in Store.KnownDevices.Where(k => !_connectedIds.Contains(k.Id)))
            {
                var flow = stored.IsInput ? DeviceFlow.Input : DeviceFlow.Output;
                var device = new AudioDevice(stored.Id, stored.Name, flow, IsConnected: false, stored.IsHeadphoneFormFactor);
                (stored.IsInput ? inputs : outputs).Add(device);
            }

            InputDevices = Store.SortByPriority(inputs, PriorityList.Input);
            SpeakerDevices = Store.SortByPriority(OfCategory(outputs, OutputCategory.Speaker), PriorityList.Speaker);
            HeadphoneDevices = Store.SortByPriority(OfCategory(outputs, OutputCategory.Headphone), PriorityList.Headphone);
            HiddenInputDevices = HiddenSpeakerDevices = HiddenHeadphoneDevices = [];
        }
        else
        {
            (InputDevices, HiddenInputDevices) = Partition(inputs, PriorityList.Input);
            (SpeakerDevices, HiddenSpeakerDevices) = Partition(OfCategory(outputs, OutputCategory.Speaker), PriorityList.Speaker);
            (HeadphoneDevices, HiddenHeadphoneDevices) = Partition(OfCategory(outputs, OutputCategory.Headphone), PriorityList.Headphone);
        }

        CurrentInputId = _service.GetDefaultDeviceId(DeviceFlow.Input);
        CurrentOutputId = _service.GetDefaultDeviceId(DeviceFlow.Output);
    }

    private List<AudioDevice> OfCategory(IEnumerable<AudioDevice> outputs, OutputCategory category) =>
        outputs.Where(d => Store.GetCategory(d) == category).ToList();

    /// <summary>Splits into visible (sorted) and hidden: plain ignored first, then never-use.</summary>
    private (IReadOnlyList<AudioDevice> Visible, IReadOnlyList<AudioDevice> Hidden) Partition(
        List<AudioDevice> devices, PriorityList list)
    {
        var visible = devices.Where(d => !Store.IsHidden(d, list) && !Store.IsNeverUse(d));
        var ignored = devices.Where(d => Store.IsHidden(d, list) && !Store.IsNeverUse(d));
        var neverUse = devices.Where(Store.IsNeverUse);
        return (Store.SortByPriority(visible, list), ignored.Concat(neverUse).ToList());
    }

    private void RefreshVolumeAndMute()
    {
        Volume = _service.GetOutputVolume();

        var muted = new HashSet<string>();
        foreach (var device in InputDevices.Concat(SpeakerDevices).Concat(HeadphoneDevices))
        {
            if (device.IsConnected && _service.IsDeviceMuted(device.Id, device.Flow))
                muted.Add(device.Id);
        }
        MutedDeviceIds = muted;
        IsActiveOutputMuted = CurrentOutputId is { } output && _service.IsDeviceMuted(output, DeviceFlow.Output);
    }

    private void RefreshAndReapply(PriorityList list)
    {
        RefreshDevices();
        if (!IsCustomMode)
        {
            if (list == PriorityList.Input) ApplyHighestPriority(PriorityList.Input);
            else ApplyHighestPriorityOutput();
        }
        RefreshVolumeAndMute();
        RaiseChanged();
    }

    // MARK: Applying defaults

    private bool IsEligible(AudioDevice device) => device.IsConnected && !Store.IsNeverUse(device);

    private void ApplyHighestPriority(PriorityList list)
    {
        if (Devices(list).FirstOrDefault(IsEligible) is { } top) Apply(top);
    }

    private void ApplyHighestPriorityOutput() => ApplyHighestPriority(CurrentMode.ToPriorityList());

    private void Apply(AudioDevice device)
    {
        var isInput = device.Flow == DeviceFlow.Input;
        var current = isInput ? CurrentInputId : CurrentOutputId;

        // Setting the default fires a default-changed notification; skipping no-op switches
        // keeps that notification from re-entering this path forever.
        if (current == device.Id || !_service.SetDefaultDevice(device.Id)) return;

        if (isInput) CurrentInputId = device.Id;
        else CurrentOutputId = device.Id;
    }

    private void SetDevices(PriorityList list, IReadOnlyList<AudioDevice> devices)
    {
        switch (list)
        {
            case PriorityList.Input: InputDevices = devices; break;
            case PriorityList.Speaker: SpeakerDevices = devices; break;
            default: HeadphoneDevices = devices; break;
        }
    }

    // MARK: System events

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        var previous = _previousConnectedIds;
        RefreshDevices();
        var newlyConnected = _connectedIds.Except(previous).ToHashSet();
        _previousConnectedIds = [.. _connectedIds];

        if (!IsCustomMode)
        {
            AutoSwitchModeIfNeeded(newlyConnected);
            ApplyHighestPriority(PriorityList.Input);
            ApplyHighestPriorityOutput();
        }
        RefreshVolumeAndMute();
        RaiseChanged();
    }

    /// <summary>
    /// Switches to headphone mode when a new headphone connects, and back to speaker mode once
    /// every headphone is gone. Nothing else changes the mode automatically.
    /// </summary>
    private void AutoSwitchModeIfNeeded(IReadOnlySet<string> newlyConnected)
    {
        var connectedHeadphones = HeadphoneDevices.Where(d => d.IsConnected).ToList();
        var hasConnectedSpeakers = SpeakerDevices.Any(d => d.IsConnected);

        if (CurrentMode != OutputCategory.Headphone && connectedHeadphones.Any(d => newlyConnected.Contains(d.Id)))
        {
            CurrentMode = Store.CurrentMode = OutputCategory.Headphone;
        }
        else if (CurrentMode == OutputCategory.Headphone && connectedHeadphones.Count == 0 && hasConnectedSpeakers)
        {
            CurrentMode = Store.CurrentMode = OutputCategory.Speaker;
        }
    }

    /// <summary>
    /// On first run, start in the mode of whatever is playing now, so launching the app never
    /// yanks audio from the headset someone is wearing over to the speakers.
    /// </summary>
    private void AdoptModeOfCurrentOutput()
    {
        if (HeadphoneDevices.Any(d => d.Id == CurrentOutputId))
            CurrentMode = Store.CurrentMode = OutputCategory.Headphone;
    }

    private void OnMuteOrVolumeChanged(object? sender, EventArgs e)
    {
        RefreshVolumeAndMute();
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _service.DevicesChanged -= OnDevicesChanged;
        _service.MuteOrVolumeChanged -= OnMuteOrVolumeChanged;
        _service.Dispose();
    }
}
