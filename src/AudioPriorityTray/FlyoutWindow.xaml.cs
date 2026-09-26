using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AudioPriorityTray.Core;
using AudioPriorityTray.ViewModels;
using static AudioPriorityTray.Native.NativeMethods;

namespace AudioPriorityTray;

/// <summary>
/// The tray flyout: a borderless Acrylic window pinned above the notification area, dismissed
/// when it loses focus, like the Windows 11 Quick Settings and volume flyouts.
/// </summary>
public partial class FlyoutWindow : Window
{
    // Clicking the tray icon while the flyout is open deactivates (hides) it first and then
    // arrives as a tray click; inside this window that click must not reopen it.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    private readonly MainViewModel _viewModel;
    private readonly Action _quit;
    private IntPtr _hwnd;
    private POINT _anchor;
    private long _hiddenAt;
    private double _entranceOffset = 12;

    public FlyoutWindow(MainViewModel viewModel, Action quit)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _quit = quit;
        SizeChanged += (_, _) => { if (IsVisible) Reposition(); };
    }

    internal void Toggle(POINT anchor)
    {
        if (IsVisible) HideFlyout();
        else if (Stopwatch.GetElapsedTime(_hiddenAt) > ReopenGuard) ShowFlyout(anchor);
    }

    internal void ShowFlyout(POINT anchor)
    {
        _anchor = anchor;
        if (!IsVisible)
        {
            // Cloaked while the first layout pass sizes the window, so it never flashes at the old spot.
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            SetDwmAttribute(hwnd, DwmwaCloak, 1);
            Show();
            UpdateLayout();
            Reposition();
            SetDwmAttribute(hwnd, DwmwaCloak, 0);
            PlayEntrance();
        }
        Activate();
        SetForegroundWindow(_hwnd);
    }

    /// <summary>Re-applies the backdrop and dark-mode frame after a Windows theme change.</summary>
    public void ApplySystemBackdrop()
    {
        if (_hwnd == IntPtr.Zero) return;

        SetDwmAttribute(_hwnd, DwmwaWindowCornerPreference, DwmwcpRound);
        SetDwmAttribute(_hwnd, DwmwaUseImmersiveDarkMode, SystemTheme.AppsUseLightTheme ? 0 : 1);
        var margins = new Margins(-1);
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);

        // Acrylic needs Windows 11 22H2+; older builds refuse the attribute and get a solid fill.
        if (SetDwmAttribute(_hwnd, DwmwaSystemBackdropType, DwmsbtTransientWindow) == 0)
        {
            HwndSource.FromHwnd(_hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
            Background = Brushes.Transparent;
        }
        else
        {
            SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // A tool window has no taskbar button and no Alt+Tab entry.
        var exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr((exStyle | WsExToolWindow) & ~WsExAppWindow));
        ApplySystemBackdrop();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        HideFlyout();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) HideFlyout();
    }

    private void HideFlyout()
    {
        if (!IsVisible) return;
        Hide();
        _hiddenAt = Stopwatch.GetTimestamp();
    }

    /// <summary>Pins the window to the work-area corner next to the taskbar, in physical pixels.</summary>
    private void Reposition()
    {
        var info = new MonitorInfo { cbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromPoint(_anchor, MonitorDefaultToNearest), ref info);
        GetWindowRect(_hwnd, out var window);

        var work = info.rcWork;
        var margin = (int)Math.Round(12 * GetDpiForWindow(_hwnd) / 96.0);
        var x = work.Right - window.Width - margin;
        var y = work.Bottom - window.Height - margin;
        _entranceOffset = 12;

        if (work.Top > info.rcMonitor.Top)
        {
            y = work.Top + margin;
            _entranceOffset = -12;
        }
        else if (work.Left > info.rcMonitor.Left)
        {
            x = work.Left + margin;
        }

        SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private void PlayEntrance()
    {
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        EntranceOffset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(_entranceOffset, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    private void OnSpeakerModeClick(object sender, RoutedEventArgs e) => _viewModel.SelectMode(OutputCategory.Speaker);

    private void OnHeadphoneModeClick(object sender, RoutedEventArgs e) => _viewModel.SelectMode(OutputCategory.Headphone);

    private void OnManualModeClick(object sender, RoutedEventArgs e) => _viewModel.ToggleCustomMode();

    private void OnEditClick(object sender, RoutedEventArgs e) => _viewModel.ToggleEditMode();

    private void OnVolumeWheel(object sender, MouseWheelEventArgs e)
    {
        _viewModel.NudgeVolume(e.Delta);
        e.Handled = true;
    }

    private void OnIgnoredClick(object sender, RoutedEventArgs e) =>
        Menus.Open(_viewModel.BuildIgnoredMenu(), (UIElement)sender, PlacementMode.Top);

    private void OnMoreClick(object sender, RoutedEventArgs e) =>
        Menus.Open(_viewModel.BuildAppMenu(_quit), (UIElement)sender, PlacementMode.Top);
}
