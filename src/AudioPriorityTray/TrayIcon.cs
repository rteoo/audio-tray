using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioPriorityTray.Native;
using static AudioPriorityTray.Native.NativeMethods;

namespace AudioPriorityTray;

/// <summary>
/// A notification-area icon on a hidden top-level window (message-only windows miss the
/// TaskbarCreated broadcast, so the icon would not come back after Explorer restarts).
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const int CallbackMessage = WmApp + 1;

    private readonly HwndSource _window;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _icon;
    private string _tooltip = "";
    private bool _added;

    public TrayIcon()
    {
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _window = new HwndSource(new HwndSourceParameters("AudioPriorityTrayNotifyWindow") { WindowStyle = 0 });
        _window.AddHook(WndProc);
    }

    /// <summary>Left click or keyboard activation.</summary>
    public event EventHandler? Invoked;

    /// <summary>Right click or the context-menu key.</summary>
    public event EventHandler? ContextMenuRequested;

    /// <summary>Taskbar theme, DPI or display layout changed; the glyph should be re-rendered.</summary>
    public event EventHandler? AppearanceChanged;

    public IntPtr Handle => _window.Handle;

    /// <summary>
    /// Sets the tooltip, and the icon when <paramref name="icon"/> is non-zero (ownership of the
    /// HICON transfers here; the previous one is destroyed).
    /// </summary>
    public void Update(string tooltip, IntPtr icon = default)
    {
        var previous = IntPtr.Zero;
        if (icon != IntPtr.Zero)
        {
            previous = _icon;
            _icon = icon;
        }
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        Notify(_added ? NimModify : NimAdd);
        if (previous != IntPtr.Zero) DestroyIcon(previous);
    }

    /// <summary>The icon's bounds in physical pixels, when the shell can report them.</summary>
    public RECT? GetBounds()
    {
        var id = new NotifyIconIdentifier
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), hWnd = _window.Handle, uID = IconId,
        };
        return Shell_NotifyIconGetRect(ref id, out var rect) == 0 ? rect : null;
    }

    /// <summary>Pixel size of a small icon on the monitor hosting the notification window.</summary>
    public int IconSize => GetSystemMetricsForDpi(SmCxSmIcon, GetDpiForWindow(_window.Handle));

    private void Notify(uint message)
    {
        var data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _window.Handle,
            uID = IconId,
            uFlags = NifMessage | NifIcon | NifTip | NifShowTip,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = _tooltip,
            szInfo = "",
            szInfoTitle = "",
        };

        if (!Shell_NotifyIcon(message, ref data))
        {
            // NIM_ADD fails while Explorer is still starting; TaskbarCreated will retry it.
            Core.Log.Error($"Shell_NotifyIcon({message}) failed");
            return;
        }
        if (message != NimAdd) return;

        _added = true;
        data.uVersion = NotifyIconVersion4;
        Shell_NotifyIcon(NimSetVersion, ref data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            // Version 4 packs the event in LOWORD(lParam).
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case NinSelect:
                case NinKeySelect:
                    Invoked?.Invoke(this, EventArgs.Empty);
                    break;
                case WmContextMenu:
                    ContextMenuRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
            handled = true;
        }
        else if (msg == (int)_taskbarCreatedMessage)
        {
            _added = false;
            Notify(NimAdd);
        }
        else if (msg is WmSettingChange or WmDpiChanged or WmDisplayChange)
        {
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = new NotifyIconData
            {
                cbSize = (uint)Marshal.SizeOf<NotifyIconData>(), hWnd = _window.Handle, uID = IconId,
                szTip = "", szInfo = "", szInfoTitle = "",
            };
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        _icon = IntPtr.Zero;
        _window.Dispose();
    }

    /// <summary>
    /// Renders a Segoe Fluent Icons glyph as a monochrome tray icon, the way Windows 11 draws
    /// its own notification-area icons. The caller owns the returned HICON.
    /// </summary>
    public static IntPtr RenderGlyph(string glyph, int size, bool lightTaskbar)
    {
        var brush = new SolidColorBrush(lightTaskbar ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Glyphs.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, brush, pixelsPerDip: 1.0);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawText(text, new System.Windows.Point((size - text.Width) / 2, (size - text.Height) / 2));

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        // CreateIconFromResourceEx accepts PNG-compressed icon images directly.
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var png = stream.ToArray();

        var icon = CreateIconFromResourceEx(png, (uint)png.Length, true, 0x00030000, size, size, 0);
        if (icon == IntPtr.Zero) throw new InvalidOperationException($"CreateIconFromResourceEx failed ({Marshal.GetLastWin32Error()}).");
        return icon;
    }
}
