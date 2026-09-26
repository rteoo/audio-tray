namespace AudioPriorityTray.Core;

/// <summary>
/// The system audio surface <see cref="AudioManager"/> drives. Events are raised on the
/// thread that owns the manager (the UI thread in the app).
/// </summary>
public interface IAudioDeviceService : IDisposable
{
    /// <summary>An endpoint was added, removed, changed state or name, or a default changed.</summary>
    event EventHandler? DevicesChanged;

    event EventHandler? MuteOrVolumeChanged;

    /// <summary>All currently active endpoints, inputs and outputs.</summary>
    IReadOnlyList<AudioDevice> GetDevices();

    string? GetDefaultDeviceId(DeviceFlow flow);

    /// <summary>Makes the endpoint the system default for every role. Returns false if Windows refused.</summary>
    bool SetDefaultDevice(string deviceId);

    /// <summary>Master volume of the default output, 0..1.</summary>
    float GetOutputVolume();

    void SetOutputVolume(float volume);

    bool IsDeviceMuted(string deviceId, DeviceFlow flow);

    void StartListening();
}
