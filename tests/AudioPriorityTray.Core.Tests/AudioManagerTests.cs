using static AudioPriorityTray.Core.Tests.FakeAudioDeviceService;

namespace AudioPriorityTray.Core.Tests;

public sealed class AudioManagerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("AudioPriorityTrayTests").FullName;
    private readonly SettingsStore _store;

    public AudioManagerTests() => _store = new SettingsStore(Path.Combine(_directory, "settings.json"));

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private AudioManager Start(FakeAudioDeviceService service) => new(service, _store);

    [Fact]
    public void Startup_applies_the_highest_priority_devices()
    {
        _store.SavePriorities([Output("monitor"), Output("desk")], PriorityList.Speaker);
        _store.SavePriorities([Input("usb-mic"), Input("webcam")], PriorityList.Input);
        var service = new FakeAudioDeviceService().With(Output("desk"), Output("monitor"), Input("webcam"), Input("usb-mic"));

        var manager = Start(service);

        Assert.Equal("monitor", service.DefaultOutput);
        Assert.Equal("usb-mic", service.DefaultInput);
        Assert.Equal("monitor", manager.CurrentOutputId);
    }

    [Fact]
    public void First_run_starts_in_the_mode_of_the_current_output()
    {
        var service = new FakeAudioDeviceService().With(Output("hp", "Headphones (G522)"), Output("desk", "Desk Speakers"));

        var manager = Start(service);

        Assert.Equal(OutputCategory.Headphone, manager.CurrentMode);
        Assert.Equal("hp", service.DefaultOutput);
    }

    [Fact]
    public void Later_runs_keep_the_saved_mode()
    {
        _store.CurrentMode = OutputCategory.Speaker;
        var service = new FakeAudioDeviceService().With(Output("hp", "Headphones (G522)"), Output("desk", "Desk Speakers"));

        var manager = new AudioManager(service, new SettingsStore(Path.Combine(_directory, "settings.json")));

        Assert.Equal(OutputCategory.Speaker, manager.CurrentMode);
        Assert.Equal("desk", service.DefaultOutput);
    }

    [Fact]
    public void Disconnecting_the_last_headphone_returns_to_speaker_mode()
    {
        var service = new FakeAudioDeviceService().With(Output("desk", "Desk Speakers"));
        var manager = Start(service);
        service.Connect(Output("bt", "AirPods"));

        service.Disconnect("bt");

        Assert.Equal(OutputCategory.Speaker, manager.CurrentMode);
        Assert.Equal("desk", service.DefaultOutput);
    }

    [Fact]
    public void Manual_mode_never_switches_automatically()
    {
        var service = new FakeAudioDeviceService().With(Output("desk", "Desk Speakers"));
        var manager = Start(service);
        manager.SetCustomMode(true);

        service.Connect(Output("bt", "AirPods"));

        Assert.Equal(OutputCategory.Speaker, manager.CurrentMode);
        Assert.Equal("desk", service.DefaultOutput);
    }

    [Fact]
    public void Automatic_mode_reverts_an_external_default_change()
    {
        _store.SavePriorities([Output("desk"), Output("monitor")], PriorityList.Speaker);
        var service = new FakeAudioDeviceService().With(Output("desk"), Output("monitor"));
        Start(service);

        service.ChangeDefaultExternally("monitor");

        Assert.Equal("desk", service.DefaultOutput);
    }

    [Fact]
    public void Default_is_not_reapplied_when_it_is_already_current()
    {
        var service = new FakeAudioDeviceService().With(Output("desk"), Input("mic"));
        Start(service);
        service.SetDefaultCalls.Clear();

        service.Connect(Output("hdmi", "Monitor Speakers"));

        Assert.Empty(service.SetDefaultCalls);
    }

    [Fact]
    public void Never_use_devices_are_skipped_by_auto_selection()
    {
        _store.SavePriorities([Output("desk"), Output("monitor")], PriorityList.Speaker);
        var service = new FakeAudioDeviceService().With(Output("monitor"), Output("desk"));
        var manager = Start(service);

        manager.SetNeverUse(Output("desk"), true);

        Assert.Equal("monitor", service.DefaultOutput);
        Assert.DoesNotContain(manager.SpeakerDevices, d => d.Id == "desk");
        Assert.Contains(manager.HiddenSpeakerDevices, d => d.Id == "desk");
    }

    [Fact]
    public void Reordering_the_active_list_applies_the_new_top_device()
    {
        var service = new FakeAudioDeviceService().With(Output("desk"), Output("monitor"));
        var manager = Start(service);
        Assert.Equal("desk", service.DefaultOutput);

        manager.MoveDevice(PriorityList.Speaker, from: 1, to: 0);

        Assert.Equal(["monitor", "desk"], manager.SpeakerDevices.Select(d => d.Id));
        Assert.Equal("monitor", service.DefaultOutput);
        Assert.Equal(["monitor", "desk"], new SettingsStore(Path.Combine(_directory, "settings.json"))
            .SortByPriority([Output("desk"), Output("monitor")], PriorityList.Speaker).Select(d => d.Id));
    }

    [Fact]
    public void Each_mode_keeps_its_own_microphone_order()
    {
        _store.CurrentMode = OutputCategory.Speaker;
        var service = new FakeAudioDeviceService().With(
            Output("desk", "Desk Speakers"), Output("hp", "Headphones (G522)"), Input("webcam"), Input("headset-mic"));
        var manager = Start(service);
        Assert.Equal("webcam", service.DefaultInput);

        manager.SetMode(OutputCategory.Headphone);
        manager.MoveDevice(PriorityList.Input, from: 1, to: 0);
        Assert.Equal("headset-mic", service.DefaultInput);

        manager.SetMode(OutputCategory.Speaker);

        Assert.Equal(["webcam", "headset-mic"], manager.InputDevices.Select(d => d.Id));
        Assert.Equal("webcam", service.DefaultInput);
    }

    [Fact]
    public void Connecting_headphones_switches_to_headphone_mode_and_its_devices()
    {
        _store.SavePriorities([Input("webcam"), Input("headset-mic")], PriorityList.Input);
        _store.CurrentMode = OutputCategory.Headphone;
        _store.SavePriorities([Input("headset-mic"), Input("webcam")], PriorityList.Input);
        _store.CurrentMode = OutputCategory.Speaker;
        var service = new FakeAudioDeviceService().With(Output("desk", "Desk Speakers"), Input("webcam"), Input("headset-mic"));
        var manager = Start(service);
        Assert.Equal("webcam", service.DefaultInput);

        service.Connect(Output("hp", "Headphones (G522)"));

        Assert.Equal(OutputCategory.Headphone, manager.CurrentMode);
        Assert.Equal(OutputCategory.Headphone, _store.CurrentMode);
        Assert.Equal("hp", service.DefaultOutput);
        Assert.Equal("headset-mic", service.DefaultInput);
        Assert.Equal(["headset-mic", "webcam"], manager.InputDevices.Select(d => d.Id));
    }

    [Fact]
    public void Reordering_an_inactive_category_does_not_switch_output()
    {
        var service = new FakeAudioDeviceService().With(Output("desk", "Desk Speakers"), Output("hp1", "Headset A"), Output("hp2", "Headset B"));
        var manager = Start(service);
        manager.SetCustomMode(true);
        manager.SetMode(OutputCategory.Speaker);

        manager.MoveDevice(PriorityList.Headphone, from: 1, to: 0);

        Assert.Equal("desk", service.DefaultOutput);
    }

    [Fact]
    public void Move_down_uses_insert_before_semantics()
    {
        var service = new FakeAudioDeviceService().With(Input("a"), Input("b"), Input("c"));
        var manager = Start(service);

        manager.MoveDevice(PriorityList.Input, from: 0, to: 2);

        Assert.Equal(["b", "a", "c"], manager.InputDevices.Select(d => d.Id));
    }

    [Fact]
    public void Ignoring_the_active_device_falls_back_to_the_next_one()
    {
        var service = new FakeAudioDeviceService().With(Output("desk"), Output("monitor"));
        var manager = Start(service);

        manager.HideDevice(Output("desk"), PriorityList.Speaker);

        Assert.Equal("monitor", service.DefaultOutput);
        Assert.Equal(["desk"], manager.HiddenSpeakerDevices.Select(d => d.Id));
        Assert.Single(manager.AllHiddenDevices);
    }

    [Fact]
    public void Moving_a_device_to_headphones_changes_its_list()
    {
        var service = new FakeAudioDeviceService().With(Output("desk", "Desk Speakers"), Output("dac", "USB DAC"));
        var manager = Start(service);

        manager.SetCategory(OutputCategory.Headphone, Output("dac", "USB DAC"));

        Assert.Equal(["desk"], manager.SpeakerDevices.Select(d => d.Id));
        Assert.Equal(["dac"], manager.HeadphoneDevices.Select(d => d.Id));
    }

    [Fact]
    public void Edit_mode_lists_disconnected_and_ignored_devices()
    {
        var service = new FakeAudioDeviceService().With(Output("desk"), Output("monitor"));
        var manager = Start(service);
        manager.HideDevice(Output("monitor"), PriorityList.Speaker);
        service.Disconnect("desk");

        manager.ToggleEditMode();

        Assert.Equivalent(new[] { ("monitor", true), ("desk", false) },
            manager.SpeakerDevices.Select(d => (d.Id, d.IsConnected)));
        Assert.Empty(manager.AllHiddenDevices);
    }

    [Fact]
    public void Forgetting_a_device_removes_it_from_edit_mode()
    {
        var service = new FakeAudioDeviceService().With(Output("desk"), Output("old"));
        var manager = Start(service);
        service.Disconnect("old");
        manager.ToggleEditMode();

        manager.ForgetDevice(manager.SpeakerDevices.Single(d => d.Id == "old"));

        Assert.Equal(["desk"], manager.SpeakerDevices.Select(d => d.Id));
    }

    [Fact]
    public void System_events_track_mute_state_and_raise_changed()
    {
        var service = new FakeAudioDeviceService().With(Output("desk"), Input("mic"));
        var manager = Start(service);
        var raised = 0;
        manager.Changed += (_, _) => raised++;

        service.Connect(Output("hdmi"));
        service.SetMuted("desk", true);
        service.SetMuted("mic", true);

        Assert.True(manager.IsActiveOutputMuted);
        Assert.True(manager.IsDeviceMuted(Output("desk")));
        Assert.True(manager.IsDeviceMuted(Input("mic")));
        Assert.Equal(3, raised);
    }

    [Fact]
    public void Volume_is_clamped_and_applied_to_the_default_output()
    {
        var service = new FakeAudioDeviceService().With(Output("desk"));
        var manager = Start(service);

        manager.SetVolume(1.4f);

        Assert.Equal(1f, manager.Volume);
        Assert.Equal(1f, service.GetOutputVolume());
    }
}
