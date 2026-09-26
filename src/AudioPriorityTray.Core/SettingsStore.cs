using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioPriorityTray.Core;

public sealed class StoredDevice
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public bool IsInput { get; set; }
    public bool IsHeadphoneFormFactor { get; set; }
    public DateTimeOffset LastSeen { get; set; }

    public string LastSeenRelative(DateTimeOffset now)
    {
        var seconds = (now - LastSeen).TotalSeconds;
        return seconds switch
        {
            < 60 => "now",
            < 3600 => $"{(int)(seconds / 60)}m ago",
            < 86400 => $"{(int)(seconds / 3600)}h ago",
            < 604800 => $"{(int)(seconds / 86400)}d ago",
            < 2592000 => $"{(int)(seconds / 604800)}w ago",
            _ => $"{(int)(seconds / 2592000)}mo ago",
        };
    }
}

/// <summary>
/// Persists priorities, categories, ignore lists and the memory of every device ever seen.
/// Every mutation is written through to disk immediately.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private readonly SettingsData _data;

    public SettingsStore(string path)
    {
        _path = path;
        IsFirstRun = !File.Exists(path);
        _data = Load(path);
    }

    /// <summary>No settings existed when the store was opened.</summary>
    public bool IsFirstRun { get; }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioPriorityTray");

    // MARK: Known devices

    public IReadOnlyList<StoredDevice> KnownDevices => _data.KnownDevices;

    public StoredDevice? GetStoredDevice(string id) => _data.KnownDevices.Find(d => d.Id == id);

    public void RememberDevices(IEnumerable<AudioDevice> connected, DateTimeOffset now)
    {
        foreach (var device in connected)
        {
            var stored = GetStoredDevice(device.Id);
            if (stored is null)
            {
                _data.KnownDevices.Add(new StoredDevice
                {
                    Id = device.Id, Name = device.Name, IsInput = device.Flow == DeviceFlow.Input,
                    IsHeadphoneFormFactor = device.IsHeadphoneFormFactor, LastSeen = now,
                });
            }
            else
            {
                stored.Name = device.Name;
                stored.IsHeadphoneFormFactor = device.IsHeadphoneFormFactor;
                stored.LastSeen = now;
            }
        }
        Save();
    }

    public void ForgetDevice(string id)
    {
        _data.KnownDevices.RemoveAll(d => d.Id == id);
        Save();
    }

    // MARK: Mode

    public OutputCategory CurrentMode
    {
        get => _data.CurrentMode;
        set { _data.CurrentMode = value; Save(); }
    }

    public bool IsCustomMode
    {
        get => _data.CustomMode;
        set { _data.CustomMode = value; Save(); }
    }

    // MARK: Categories

    public OutputCategory GetCategory(AudioDevice device)
    {
        if (_data.DeviceCategories.TryGetValue(device.Id, out var category)) return category;
        return device.IsHeadphoneFormFactor || HeadphoneDetection.IsHeadphone(device.Name)
            ? OutputCategory.Headphone
            : OutputCategory.Speaker;
    }

    public void SetCategory(OutputCategory category, AudioDevice device)
    {
        _data.DeviceCategories[device.Id] = category;
        Save();
    }

    /// <summary>The list a device belongs to: inputs by flow, outputs by category.</summary>
    public PriorityList ListFor(AudioDevice device) =>
        device.Flow == DeviceFlow.Input ? PriorityList.Input : GetCategory(device).ToPriorityList();

    // MARK: Never use (never auto-selected)

    public bool IsNeverUse(AudioDevice device) => _data.NeverUse.Contains(device.Id);

    public void SetNeverUse(AudioDevice device, bool neverUse)
    {
        _data.NeverUse.Remove(device.Id);
        if (neverUse) _data.NeverUse.Add(device.Id);
        Save();
    }

    // MARK: Hidden devices (per list)

    public bool IsHidden(AudioDevice device) => IsHidden(device, ListFor(device));

    public bool IsHidden(AudioDevice device, PriorityList list) => HiddenIds(list).Contains(device.Id);

    public void HideDevice(AudioDevice device) => HideDevice(device, ListFor(device));

    public void HideDevice(AudioDevice device, PriorityList list)
    {
        var hidden = HiddenIds(list);
        if (hidden.Contains(device.Id)) return;
        hidden.Add(device.Id);
        Save();
    }

    public void UnhideDevice(AudioDevice device) => UnhideDevice(device, ListFor(device));

    public void UnhideDevice(AudioDevice device, PriorityList list)
    {
        if (HiddenIds(list).Remove(device.Id)) Save();
    }

    // MARK: Priorities

    /// <summary>Stable sort by saved rank; devices never ranked keep their order after ranked ones.</summary>
    public IReadOnlyList<AudioDevice> SortByPriority(IEnumerable<AudioDevice> devices, PriorityList list)
    {
        var ranks = Priorities(list);
        return devices.OrderBy(d => ranks.IndexOf(d.Id) is var i and >= 0 ? i : int.MaxValue).ToList();
    }

    /// <summary>
    /// Saves a reordering of <paramref name="devices"/>. Devices absent from the list (disconnected
    /// or ignored while not in edit mode) keep their saved slots, so reordering the visible devices
    /// never discards the rank of one that is currently unplugged.
    /// </summary>
    public void SavePriorities(IEnumerable<AudioDevice> devices, PriorityList list)
    {
        var ranks = Priorities(list);
        var merged = MergeOrder(ranks, devices.Select(d => d.Id).ToList());
        ranks.Clear();
        ranks.AddRange(merged);
        Save();
    }

    internal static List<string> MergeOrder(IReadOnlyList<string> saved, IReadOnlyList<string> reordered)
    {
        var moving = reordered.ToHashSet();
        var result = new List<string>(saved.Count + reordered.Count);
        var next = 0;
        foreach (var id in saved.Distinct())
            result.Add(moving.Contains(id) ? reordered[next++] : id);
        result.AddRange(reordered.Skip(next));
        return result;
    }

    // MARK: Persistence

    private List<string> Priorities(PriorityList list) => list switch
    {
        PriorityList.Input => _data.InputPriorities,
        PriorityList.Speaker => _data.SpeakerPriorities,
        _ => _data.HeadphonePriorities,
    };

    private List<string> HiddenIds(PriorityList list) => list switch
    {
        PriorityList.Input => _data.HiddenMics,
        PriorityList.Speaker => _data.HiddenSpeakers,
        _ => _data.HiddenHeadphones,
    };

    private static SettingsData Load(string path)
    {
        if (!File.Exists(path)) return new SettingsData();
        try
        {
            return JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(path), JsonOptions) ?? new SettingsData();
        }
        catch (JsonException ex)
        {
            // Keep the unreadable file for inspection instead of crashing on every launch.
            var backup = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, backup);
            Log.Error($"Settings were unreadable ({ex.Message}); moved to {backup} and started fresh.");
            return new SettingsData();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }

    private sealed class SettingsData
    {
        public List<string> InputPriorities { get; set; } = [];
        public List<string> SpeakerPriorities { get; set; } = [];
        public List<string> HeadphonePriorities { get; set; } = [];
        public Dictionary<string, OutputCategory> DeviceCategories { get; set; } = [];
        public OutputCategory CurrentMode { get; set; } = OutputCategory.Speaker;
        public bool CustomMode { get; set; }
        public List<string> HiddenMics { get; set; } = [];
        public List<string> HiddenSpeakers { get; set; } = [];
        public List<string> HiddenHeadphones { get; set; } = [];
        public List<string> NeverUse { get; set; } = [];
        public List<StoredDevice> KnownDevices { get; set; } = [];
    }
}
