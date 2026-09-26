using Microsoft.Win32;

namespace AudioPriorityTray;

internal static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Theme of apps (the flyout).</summary>
    public static bool AppsUseLightTheme => ReadFlag("AppsUseLightTheme", defaultValue: true);

    /// <summary>Theme of the shell (taskbar), which the tray glyph must contrast with.</summary>
    public static bool SystemUsesLightTheme => ReadFlag("SystemUsesLightTheme", defaultValue: false);

    private static bool ReadFlag(string name, bool defaultValue)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(name) is int value ? value != 0 : defaultValue;
    }
}
