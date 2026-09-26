namespace AudioPriorityTray.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("AudioPriorityTrayTests").FullName;

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Settings_survive_a_reload()
    {
        var headphones = FakeAudioDeviceService.Output("hp", "Studio Monitors");
        var store = new SettingsStore(SettingsPath);
        store.SetCategory(OutputCategory.Headphone, headphones);
        store.HideDevice(headphones, PriorityList.Speaker);
        store.SetNeverUse(headphones, true);
        store.CurrentMode = OutputCategory.Headphone;
        store.IsCustomMode = true;
        store.SavePriorities([FakeAudioDeviceService.Output("b"), FakeAudioDeviceService.Output("a")], PriorityList.Speaker);

        var reloaded = new SettingsStore(SettingsPath);

        Assert.Equal(OutputCategory.Headphone, reloaded.GetCategory(headphones));
        Assert.True(reloaded.IsHidden(headphones, PriorityList.Speaker));
        Assert.True(reloaded.IsNeverUse(headphones));
        Assert.Equal(OutputCategory.Headphone, reloaded.CurrentMode);
        Assert.True(reloaded.IsCustomMode);
        Assert.Equal(["b", "a"], reloaded.SortByPriority(
            [FakeAudioDeviceService.Output("a"), FakeAudioDeviceService.Output("b")], PriorityList.Speaker).Select(d => d.Id));
    }

    [Fact]
    public void Unreadable_settings_are_set_aside_instead_of_crashing()
    {
        File.WriteAllText(SettingsPath, "{ not json");

        var store = new SettingsStore(SettingsPath);

        Assert.Equal(OutputCategory.Speaker, store.CurrentMode);
        Assert.Single(Directory.GetFiles(_directory, "settings.json.corrupt-*"));
    }

    [Theory]
    [InlineData("Headphones (WH-1000XM4)", OutputCategory.Headphone)]
    [InlineData("Headset Earphone (Jabra Evolve2 65)", OutputCategory.Headphone)]
    [InlineData("AirPods Pro", OutputCategory.Headphone)]
    [InlineData("Speakers (Realtek(R) Audio)", OutputCategory.Speaker)]
    public void Uncategorized_devices_are_classified_by_name(string name, OutputCategory expected)
    {
        var store = new SettingsStore(SettingsPath);

        Assert.Equal(expected, store.GetCategory(FakeAudioDeviceService.Output("id", name)));
    }

    [Fact]
    public void Headphone_form_factor_wins_over_a_localized_name()
    {
        var store = new SettingsStore(SettingsPath);

        var headset = new AudioDevice("id", "Fones de ouvido (G522 LIGHTSPEED)", DeviceFlow.Output, IsHeadphoneFormFactor: true);

        Assert.Equal(OutputCategory.Headphone, store.GetCategory(headset));
    }

    [Fact]
    public void Explicit_category_overrides_name_detection()
    {
        var store = new SettingsStore(SettingsPath);
        var airpods = FakeAudioDeviceService.Output("id", "AirPods");

        store.SetCategory(OutputCategory.Speaker, airpods);

        Assert.Equal(OutputCategory.Speaker, store.GetCategory(airpods));
    }

    [Fact]
    public void Unranked_devices_sort_after_ranked_ones_in_their_original_order()
    {
        var store = new SettingsStore(SettingsPath);
        store.SavePriorities([FakeAudioDeviceService.Input("c")], PriorityList.Input);

        var sorted = store.SortByPriority(
            [FakeAudioDeviceService.Input("x"), FakeAudioDeviceService.Input("y"), FakeAudioDeviceService.Input("c")],
            PriorityList.Input);

        Assert.Equal(["c", "x", "y"], sorted.Select(d => d.Id));
    }

    [Theory]
    [InlineData(new[] { "a", "x", "b", "c" }, new[] { "c", "a", "b" }, new[] { "c", "x", "a", "b" })]
    [InlineData(new string[0], new[] { "a", "b" }, new[] { "a", "b" })]
    [InlineData(new[] { "a", "b" }, new[] { "b", "new", "a" }, new[] { "b", "new", "a" })]
    [InlineData(new[] { "x", "a" }, new[] { "a" }, new[] { "x", "a" })]
    public void Reordering_visible_devices_keeps_absent_devices_in_their_slots(string[] saved, string[] reordered, string[] expected)
    {
        Assert.Equal(expected, SettingsStore.MergeOrder(saved, reordered));
    }

    [Fact]
    public void Remembered_devices_update_name_and_last_seen()
    {
        var store = new SettingsStore(SettingsPath);
        var first = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        store.RememberDevices([FakeAudioDeviceService.Output("id", "Old name")], first);

        store.RememberDevices([FakeAudioDeviceService.Output("id", "New name")], first.AddDays(3));

        var stored = Assert.Single(store.KnownDevices);
        Assert.Equal("New name", stored.Name);
        Assert.Equal("3d ago", stored.LastSeenRelative(first.AddDays(6)));
    }
}
