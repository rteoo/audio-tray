namespace AudioPriorityTray.Core.Tests;

/// <summary>In-memory audio system: devices plug in and out, defaults follow like Windows does.</summary>
internal sealed class FakeAudioDeviceService : IAudioDeviceService
{
    private readonly List<AudioDevice> _devices = [];
    private readonly Dictionary<string, float> _volumes = [];
    private readonly HashSet<string> _muted = [];

    public event EventHandler? DevicesChanged;
    public event EventHandler? MuteOrVolumeChanged;

    public string? DefaultInput { get; set; }
    public string? DefaultOutput { get; set; }
    public List<string> SetDefaultCalls { get; } = [];

    public static AudioDevice Output(string id, string? name = null) => new(id, name ?? id, DeviceFlow.Output);

    public static AudioDevice Input(string id, string? name = null) => new(id, name ?? id, DeviceFlow.Input);

    /// <summary>Adds devices without raising events (initial state before the manager starts).</summary>
    public FakeAudioDeviceService With(params AudioDevice[] devices)
    {
        foreach (var device in devices) Add(device);
        return this;
    }

    public void Connect(AudioDevice device)
    {
        Add(device);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Disconnect(string id)
    {
        _devices.RemoveAll(d => d.Id == id);
        // Windows falls back to another active endpoint when the default disappears.
        if (DefaultInput == id) DefaultInput = _devices.FirstOrDefault(d => d.Flow == DeviceFlow.Input)?.Id;
        if (DefaultOutput == id) DefaultOutput = _devices.FirstOrDefault(d => d.Flow == DeviceFlow.Output)?.Id;
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Simulates the user picking a default elsewhere (Windows Settings, another app).</summary>
    public void ChangeDefaultExternally(string id)
    {
        SetDefault(id);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMuted(string id, bool muted)
    {
        if (muted) _muted.Add(id);
        else _muted.Remove(id);
        MuteOrVolumeChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<AudioDevice> GetDevices() => _devices.ToList();

    public string? GetDefaultDeviceId(DeviceFlow flow) => flow == DeviceFlow.Input ? DefaultInput : DefaultOutput;

    public bool SetDefaultDevice(string deviceId)
    {
        if (_devices.All(d => d.Id != deviceId)) return false;
        SetDefaultCalls.Add(deviceId);
        SetDefault(deviceId);
        return true;
    }

    public float GetOutputVolume() => DefaultOutput is { } id ? _volumes.GetValueOrDefault(id, 0.5f) : 0f;

    public void SetOutputVolume(float volume)
    {
        if (DefaultOutput is { } id) _volumes[id] = volume;
    }

    public bool IsDeviceMuted(string deviceId, DeviceFlow flow) =>
        _muted.Contains(deviceId) || (flow == DeviceFlow.Output && _volumes.GetValueOrDefault(deviceId, 0.5f) < 0.01f);

    public void StartListening()
    {
    }

    public void Dispose()
    {
    }

    private void Add(AudioDevice device)
    {
        _devices.Add(device);
        if (device.Flow == DeviceFlow.Input) DefaultInput ??= device.Id;
        else DefaultOutput ??= device.Id;
    }

    private void SetDefault(string id)
    {
        if (_devices.First(d => d.Id == id).Flow == DeviceFlow.Input) DefaultInput = id;
        else DefaultOutput = id;
    }
}
