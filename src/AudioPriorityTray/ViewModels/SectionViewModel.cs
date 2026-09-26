using System.Collections.ObjectModel;
using AudioPriorityTray.Core;

namespace AudioPriorityTray.ViewModels;

/// <summary>One priority list (speakers, headphones or microphones) as shown in the flyout.</summary>
public sealed class SectionViewModel(AudioManager manager, PriorityList list, string title, string glyph) : ObservableObject
{
    private bool _isVisible = true;
    private bool _isEmpty = true;

    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public ObservableCollection<DeviceRowViewModel> Rows { get; } = [];
    public bool IsVisible { get => _isVisible; private set => Set(ref _isVisible, value); }
    public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }

    public void Sync(bool isVisible)
    {
        IsVisible = isVisible;

        var devices = manager.Devices(list);
        var currentId = list == PriorityList.Input ? manager.CurrentInputId : manager.CurrentOutputId;

        // Update rows in place when the order is unchanged so hover state and bindings survive.
        if (!Rows.Select(r => r.Device.Id).SequenceEqual(devices.Select(d => d.Id)))
        {
            Rows.Clear();
            foreach (var device in devices) Rows.Add(new DeviceRowViewModel(device));
        }
        for (var i = 0; i < devices.Count; i++)
            Rows[i].Update(manager, devices[i], i, currentId, list);

        IsEmpty = devices.Count == 0;
    }

    public void Move(int from, int to) => manager.MoveDevice(list, from, to);

    /// <summary>
    /// Manual mode makes the clicked device the default; automatic mode promotes it to top
    /// priority, which makes it the default through the priority rules.
    /// </summary>
    public void Activate(DeviceRowViewModel row)
    {
        if (!row.Device.IsConnected) return;
        if (manager.IsCustomMode) manager.SelectDevice(row.Device);
        else if (Rows.IndexOf(row) is > 0 and var index) manager.MoveDevice(list, index, 0);
    }

    public IReadOnlyList<MenuEntry> BuildMenu(DeviceRowViewModel row)
    {
        var device = row.Device;
        var index = Rows.IndexOf(row);
        var entries = new List<MenuEntry>();

        if (list != PriorityList.Input)
        {
            if (list != PriorityList.Speaker)
                entries.Add(new("Move to Speakers", Glyphs.Volume, () => manager.SetCategory(OutputCategory.Speaker, device)));
            if (list != PriorityList.Headphone)
                entries.Add(new("Move to Headphones", Glyphs.Headphone, () => manager.SetCategory(OutputCategory.Headphone, device)));
            entries.Add(MenuEntry.Separator);
        }

        if (manager.IsDeviceIgnored(device, list))
        {
            entries.Add(new("Stop ignoring", Glyphs.View, () => manager.UnhideDevice(device, list)));
        }
        else
        {
            var noun = list switch { PriorityList.Input => "microphone", PriorityList.Speaker => "speaker", _ => "headphone" };
            entries.Add(new($"Ignore as {noun}", Glyphs.Hide, () => manager.HideDevice(device, list)));
            if (device.Flow == DeviceFlow.Output)
                entries.Add(new("Ignore entirely", Glyphs.Hide, () => manager.HideDeviceEntirely(device)));
        }

        entries.Add(MenuEntry.Separator);
        if (index > 0) entries.Add(new("Move up", Glyphs.Up, () => Move(index, index - 1)));
        if (index < Rows.Count - 1) entries.Add(new("Move down", Glyphs.Down, () => Move(index, index + 2)));

        entries.Add(MenuEntry.Separator);
        if (device.IsConnected)
        {
            var neverUse = manager.IsNeverUse(device);
            entries.Add(neverUse
                ? new("Allow use", Glyphs.Accept, () => manager.SetNeverUse(device, false))
                : new("Never use", Glyphs.Blocked, () => manager.SetNeverUse(device, true)));
        }
        else
        {
            entries.Add(new("Forget device", Glyphs.Delete, () => manager.ForgetDevice(device)));
        }
        return entries;
    }
}
