using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using AudioPriorityTray.Core;
using AudioPriorityTray.ViewModels;
using static AudioPriorityTray.Native.NativeMethods;

namespace AudioPriorityTray;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AudioManager? _manager;
    private MainViewModel? _viewModel;
    private FlyoutWindow? _flyout;
    private TrayIcon? _tray;
    private (string Glyph, bool LightTaskbar, int Size) _renderedIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.FilePath = Path.Combine(SettingsStore.DefaultDirectory, "app.log");
        DispatcherUnhandledException += OnUnhandledException;

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\AudioPriorityTray", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        var service = new WindowsAudioDeviceService(new DispatcherSynchronizationContext(Dispatcher));
        _manager = new AudioManager(service, new SettingsStore(Path.Combine(SettingsStore.DefaultDirectory, "settings.json")));
        _viewModel = new MainViewModel(_manager);
        _flyout = new FlyoutWindow(_viewModel, Quit);

        _tray = new TrayIcon();
        _tray.Invoked += (_, _) => _flyout.Toggle(TrayAnchor());
        _tray.ContextMenuRequested += (_, _) => ShowTrayMenu();
        _tray.AppearanceChanged += (_, _) =>
        {
            _renderedIcon = default;
            UpdateTrayIcon();
            _flyout.ApplySystemBackdrop();
        };
        _manager.Changed += (_, _) => UpdateTrayIcon();
        UpdateTrayIcon();
        Log.Info("Started");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _manager?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void Quit() => Shutdown();

    private void UpdateTrayIcon()
    {
        if (_manager is null || _tray is null) return;

        var glyph = _manager.IsActiveOutputMuted ? Glyphs.Mute
            : !_manager.IsCustomMode && _manager.CurrentMode == OutputCategory.Headphone ? Glyphs.Headphone
            : Glyphs.VolumeLevel(_manager.Volume);
        (string Glyph, bool LightTaskbar, int Size) rendered = (glyph, SystemTheme.SystemUsesLightTheme, _tray.IconSize);

        var mode = _manager.IsCustomMode ? "Manual" : _manager.CurrentMode == OutputCategory.Headphone ? "Headphones" : "Speakers";
        var output = _manager.CurrentOutputName ?? "No output device";
        var volume = _manager.IsActiveOutputMuted ? "muted" : $"{Math.Round(_manager.Volume * 100)}%";
        var tooltip = $"{output}: {volume}\nMode: {mode}";

        // Only rebuild the HICON when the picture changes, not on every volume tick.
        var icon = rendered == _renderedIcon ? IntPtr.Zero : TrayIcon.RenderGlyph(glyph, rendered.Size, rendered.LightTaskbar);
        _renderedIcon = rendered;
        _tray.Update(tooltip, icon);
    }

    private POINT TrayAnchor()
    {
        if (_tray?.GetBounds() is { } bounds)
            return new POINT(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        GetCursorPos(out var cursor);
        return cursor;
    }

    private void ShowTrayMenu()
    {
        if (_viewModel is null || _flyout is null) return;
        var menu = Menus.Open(_viewModel.BuildAppMenu(Quit, () => _flyout.ShowFlyout(TrayAnchor())), null, PlacementMode.MousePoint);

        // A menu from a background process only dismisses on outside clicks once it owns the foreground.
        if (PresentationSource.FromVisual(menu) is HwndSource source) SetForegroundWindow(source.Handle);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error($"Unhandled: {e.Exception}");
        MessageBox.Show($"AudioTray hit an unexpected error and will close.\n\n{e.Exception.Message}\n\nDetails: {Log.FilePath}",
            "AudioTray", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }
}
