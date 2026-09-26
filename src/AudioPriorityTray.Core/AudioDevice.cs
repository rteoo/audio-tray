namespace AudioPriorityTray.Core;

public enum DeviceFlow { Input, Output }

public enum OutputCategory { Speaker, Headphone }

/// <summary>The three independently ordered priority lists (and their matching ignore lists).</summary>
public enum PriorityList { Input, Speaker, Headphone }

/// <summary>
/// An audio endpoint. Windows endpoint IDs are stable across reconnects, so <see cref="Id"/>
/// is both the live handle and the persistent key. <see cref="IsHeadphoneFormFactor"/> is the
/// driver-reported form factor (headphones or headset), which unlike the name is not localized.
/// </summary>
public sealed record AudioDevice(
    string Id, string Name, DeviceFlow Flow, bool IsConnected = true, bool IsHeadphoneFormFactor = false);

public static class PriorityListExtensions
{
    public static PriorityList ToPriorityList(this OutputCategory category) =>
        category == OutputCategory.Headphone ? PriorityList.Headphone : PriorityList.Speaker;

    public static OutputCategory? ToCategory(this PriorityList list) => list switch
    {
        PriorityList.Speaker => OutputCategory.Speaker,
        PriorityList.Headphone => OutputCategory.Headphone,
        _ => null,
    };
}
