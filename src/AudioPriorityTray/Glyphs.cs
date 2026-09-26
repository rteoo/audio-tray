using System.Windows.Media;

namespace AudioPriorityTray;

/// <summary>Segoe Fluent Icons code points (Segoe MDL2 Assets carries the same ones on Windows 10).</summary>
internal static class Glyphs
{
    public static readonly FontFamily FontFamily = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public const string Volume = "\uE767";
    public const string Volume0 = "\uE992";
    public const string Volume1 = "\uE993";
    public const string Volume2 = "\uE994";
    public const string Volume3 = "\uE995";
    public const string Mute = "\uE74F";
    public const string Headphone = "\uE7F6";
    public const string Microphone = "\uE720";
    public const string Accept = "\uE8FB";
    public const string Hide = "\uED1A";
    public const string View = "\uE890";
    public const string Delete = "\uE74D";
    public const string Blocked = "\uE733";
    public const string Disconnected = "\uE8CD";
    public const string Up = "\uE70E";
    public const string Down = "\uE70D";
    public const string Settings = "\uE713";
    public const string Close = "\uE711";

    public static string VolumeLevel(float volume) => volume switch
    {
        <= 0f => Volume0,
        < 0.33f => Volume1,
        < 0.66f => Volume2,
        _ => Volume3,
    };
}
