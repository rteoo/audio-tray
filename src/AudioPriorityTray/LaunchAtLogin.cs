using Microsoft.Win32;

namespace AudioPriorityTray;

/// <summary>Start-with-Windows via the per-user Run key, honoring Task Manager's startup toggle.</summary>
internal static class LaunchAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "AudioPriorityTray";

    public static bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string) return false;

            // Task Manager disables startup apps by writing an odd first byte here instead of
            // removing the Run value.
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);

        if (enabled) run.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else run.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
