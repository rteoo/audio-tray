using System.Runtime.InteropServices;
using AudioPriorityTray.Core.Interop;

namespace AudioPriorityTray.Core;

/// <summary>
/// <see cref="IAudioDeviceService"/> over the Windows Core Audio APIs. Core Audio calls back on
/// its own worker threads; every notification is coalesced and posted to <paramref name="context"/>.
/// </summary>
public sealed class WindowsAudioDeviceService(SynchronizationContext context) : IAudioDeviceService
{
    private static readonly ERole[] AllRoles = [ERole.Console, ERole.Multimedia, ERole.Communications];

    private readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
    private readonly IPolicyConfig _policyConfig = (IPolicyConfig)new PolicyConfigClientComObject();
    private readonly Dictionary<string, VolumeWatch> _volumeWatches = [];
    private NotificationClient? _notificationClient;
    private Guid _eventContext = Guid.NewGuid();
    private int _devicesChangedPending;
    private int _volumeChangedPending;

    public event EventHandler? DevicesChanged;
    public event EventHandler? MuteOrVolumeChanged;

    public IReadOnlyList<AudioDevice> GetDevices()
    {
        var devices = new List<AudioDevice>();
        foreach (var flow in (DeviceFlow[])[DeviceFlow.Input, DeviceFlow.Output])
        {
            foreach (var endpoint in ActiveEndpoints(flow))
            {
                var id = endpoint.GetId();
                var properties = endpoint.OpenPropertyStore(CoreAudioConstants.StgmRead);
                var name = ReadProperty(properties, CoreAudioConstants.DeviceFriendlyName,
                    v => v.VarType == CoreAudioConstants.VtLpwstr ? Marshal.PtrToStringUni(v.PointerValue) : null);
                var formFactor = ReadProperty(properties, CoreAudioConstants.EndpointFormFactor,
                    v => v.VarType == CoreAudioConstants.VtUi4 ? v.UIntValue : (uint?)null);
                var isHeadphone = formFactor is CoreAudioConstants.FormFactorHeadphones or CoreAudioConstants.FormFactorHeadset;
                devices.Add(new AudioDevice(id, name ?? id, flow, IsHeadphoneFormFactor: isHeadphone));
            }
        }
        return devices;
    }

    public string? GetDefaultDeviceId(DeviceFlow flow)
    {
        var hr = _enumerator.GetDefaultAudioEndpoint(ToDataFlow(flow), ERole.Console, out var endpoint);
        if (hr == CoreAudioConstants.ENotFound) return null;
        Marshal.ThrowExceptionForHR(hr);
        return endpoint!.GetId();
    }

    public bool SetDefaultDevice(string deviceId)
    {
        foreach (var role in AllRoles)
        {
            var hr = _policyConfig.SetDefaultEndpoint(deviceId, role);
            if (hr < 0)
            {
                // Expected when the device vanished between enumeration and the switch.
                Log.Error($"SetDefaultEndpoint({deviceId}, {role}) failed: 0x{hr:X8}");
                return false;
            }
        }
        return true;
    }

    public float GetOutputVolume() =>
        GetDefaultDeviceId(DeviceFlow.Output) is { } id && GetEndpointVolume(id) is { } volume
            ? volume.GetMasterVolumeLevelScalar()
            : 0f;

    public void SetOutputVolume(float volume)
    {
        if (GetDefaultDeviceId(DeviceFlow.Output) is { } id && GetEndpointVolume(id) is { } endpoint)
            endpoint.SetMasterVolumeLevelScalar(Math.Clamp(volume, 0f, 1f), ref _eventContext);
    }

    public bool IsDeviceMuted(string deviceId, DeviceFlow flow)
    {
        if (GetEndpointVolume(deviceId) is not { } volume) return false;
        if (volume.GetMute()) return true;
        // Some devices report "muted" only as a zero master level.
        return flow == DeviceFlow.Output && volume.GetMasterVolumeLevelScalar() < 0.01f;
    }

    public void StartListening()
    {
        if (_notificationClient is not null) return;
        _notificationClient = new NotificationClient(QueueDevicesChanged);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
        SyncVolumeWatches();
    }

    public void Dispose()
    {
        if (_notificationClient is not null)
        {
            _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            _notificationClient = null;
        }
        foreach (var id in _volumeWatches.Keys.ToList()) Unwatch(id);
    }

    // MARK: Notifications

    private void QueueDevicesChanged()
    {
        // A single connect fires a burst (state, default x3 roles, properties); handle it once.
        if (Interlocked.Exchange(ref _devicesChangedPending, 1) == 1) return;
        context.Post(_ =>
        {
            Volatile.Write(ref _devicesChangedPending, 0);
            SyncVolumeWatches();
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }, null);
    }

    private void QueueVolumeChanged()
    {
        if (Interlocked.Exchange(ref _volumeChangedPending, 1) == 1) return;
        context.Post(_ =>
        {
            Volatile.Write(ref _volumeChangedPending, 0);
            MuteOrVolumeChanged?.Invoke(this, EventArgs.Empty);
        }, null);
    }

    /// <summary>Keeps one mute/volume subscription per active endpoint.</summary>
    private void SyncVolumeWatches()
    {
        var active = GetDevices().Select(d => d.Id).ToHashSet();
        foreach (var id in _volumeWatches.Keys.Where(id => !active.Contains(id)).ToList())
            Unwatch(id);

        foreach (var id in active.Where(id => !_volumeWatches.ContainsKey(id)))
        {
            if (ActivateEndpointVolume(id) is not { } volume) continue;
            var callback = new VolumeCallback(QueueVolumeChanged);
            volume.RegisterControlChangeNotify(callback);
            _volumeWatches[id] = new VolumeWatch(volume, callback);
        }
    }

    private void Unwatch(string id)
    {
        var watch = _volumeWatches[id];
        _volumeWatches.Remove(id);
        try
        {
            watch.Volume.UnregisterControlChangeNotify(watch.Callback);
        }
        catch (COMException ex)
        {
            // The endpoint is already gone, which also drops its subscriptions.
            Log.Info($"Volume watch for {id} ended with the device: 0x{ex.HResult:X8}");
        }
    }

    // MARK: Endpoint helpers

    private IEnumerable<IMMDevice> ActiveEndpoints(DeviceFlow flow)
    {
        var collection = _enumerator.EnumAudioEndpoints(ToDataFlow(flow), CoreAudioConstants.DeviceStateActive);
        var count = collection.GetCount();
        for (uint i = 0; i < count; i++) yield return collection.Item(i);
    }

    private IAudioEndpointVolume? GetEndpointVolume(string id) =>
        _volumeWatches.TryGetValue(id, out var watch) ? watch.Volume : ActivateEndpointVolume(id);

    private IAudioEndpointVolume? ActivateEndpointVolume(string id)
    {
        try
        {
            var iid = CoreAudioConstants.IidAudioEndpointVolume;
            return (IAudioEndpointVolume)_enumerator.GetDevice(id).Activate(ref iid, CoreAudioConstants.ClsctxAll, IntPtr.Zero);
        }
        catch (COMException ex)
        {
            Log.Info($"No volume control for {id}: 0x{ex.HResult:X8}");
            return null;
        }
    }

    private static T ReadProperty<T>(IPropertyStore store, PropertyKey key, Func<PropVariant, T> read)
    {
        store.GetValue(ref key, out var value);
        try
        {
            return read(value);
        }
        finally
        {
            Ole32.PropVariantClear(ref value);
        }
    }

    private static EDataFlow ToDataFlow(DeviceFlow flow) =>
        flow == DeviceFlow.Input ? EDataFlow.Capture : EDataFlow.Render;

    private sealed record VolumeWatch(IAudioEndpointVolume Volume, VolumeCallback Callback);

    private sealed class VolumeCallback(Action onNotify) : IAudioEndpointVolumeCallback
    {
        public void OnNotify(IntPtr notifyData) => onNotify();
    }

    private sealed class NotificationClient(Action onChange) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, uint newState) => onChange();

        public void OnDeviceAdded(string deviceId) => onChange();

        public void OnDeviceRemoved(string deviceId) => onChange();

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) => onChange();

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
            // Property churn is constant (levels, formats); only a rename changes what we show.
            if (key.FormatId == CoreAudioConstants.DeviceFriendlyName.FormatId &&
                key.PropertyId == CoreAudioConstants.DeviceFriendlyName.PropertyId)
                onChange();
        }
    }
}
