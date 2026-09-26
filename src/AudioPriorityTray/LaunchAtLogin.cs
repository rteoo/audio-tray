using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AudioPriorityTray;

/// <summary>Start-with-Windows via the per-user Run key, honoring Task Manager's startup toggle.</summary>
internal static class LaunchAtLogin
{
    private const int AppModelErrorNoPackage = 15700;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "AudioPriorityTray";

    /// <summary>
    /// Store (MSIX) builds can't use the Run key: a packaged app's registry writes are virtualized
    /// and its install path changes on update. Their manifest declares a startup task instead,
    /// which the user turns on in Settings > Apps > Startup.
    /// </summary>
    public static bool IsManagedByWindows { get; } = IsPackaged();

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

    private static bool IsPackaged()
    {
        var length = 0u;
        return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, char[]? packageFullName);
}
