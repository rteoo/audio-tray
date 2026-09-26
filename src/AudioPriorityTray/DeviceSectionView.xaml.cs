using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AudioPriorityTray.ViewModels;

namespace AudioPriorityTray;

/// <summary>
/// One device section. Rows are activated by click or Enter/Space, reordered by drag or
/// Ctrl+Up/Down (the keyboard route the design system requires for every drag), and expose
/// their actions through the always-visible "..." button, right-click, or Shift+F10.
/// </summary>
public partial class DeviceSectionView : UserControl
{
    private DeviceRowViewModel? _pressed;
    private Point _pressPoint;
    private bool _dragging;
    private int _target = -1;

    public DeviceSectionView() => InitializeComponent();

    private SectionViewModel Section => (SectionViewModel)DataContext;

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
        _pressed = RowOf(e.OriginalSource);
        _pressPoint = e.GetPosition(Items);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(Items);

        if (!_dragging)
        {
            if (Math.Abs(position.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragging = true;
            _pressed.IsDragging = true;
            Items.CaptureMouse();
        }

        _target = TargetIndexAt(position.Y);
        ShowDropIndicator();
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        var row = _pressed;
        if (row is null) return;

        if (_dragging)
        {
            var from = Section.Rows.IndexOf(row);
            var to = _target;
            EndDrag();
            if (from >= 0 && to >= 0 && to != from && to != from + 1) Section.Move(from, to);
        }
        else
        {
            _pressed = null;
            if (RowOf(e.OriginalSource) == row) Section.Activate(row);
        }
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dragging) EndDrag();
    }

    private void EndDrag()
    {
        // Cleared before releasing capture, which re-enters through OnLostMouseCapture.
        _dragging = false;
        if (_pressed is not null) _pressed.IsDragging = false;
        _pressed = null;
        _target = -1;
        DropIndicator.Visibility = Visibility.Collapsed;
        if (Items.IsMouseCaptured) Items.ReleaseMouseCapture();
    }

    /// <summary>Insertion index for a pointer at <paramref name="y"/>: before the first row whose midpoint is below it.</summary>
    private int TargetIndexAt(double y)
    {
        for (var i = 0; i < Section.Rows.Count; i++)
        {
            if (Container(i) is { } row && y < Top(row) + row.ActualHeight / 2) return i;
        }
        return Section.Rows.Count;
    }

    private void ShowDropIndicator()
    {
        var from = _pressed is null ? -1 : Section.Rows.IndexOf(_pressed);
        var count = Section.Rows.Count;
        if (_target < 0 || _target == from || _target == from + 1 || count == 0)
        {
            DropIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var y = _target < count
            ? Top(Container(_target)!)
            : Top(Container(count - 1)!) + Container(count - 1)!.ActualHeight;
        Canvas.SetLeft(DropIndicator, 4);
        Canvas.SetTop(DropIndicator, y - 1);
        DropIndicator.Width = Math.Max(0, Items.ActualWidth - 8);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private FrameworkElement? Container(int index) =>
        Items.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;

    private double Top(FrameworkElement element) => element.TranslatePoint(new Point(0, 0), Items).Y;

    private bool IsInsideButton(DependencyObject? element)
    {
        for (var current = element; current is not null && current != Items; current = ParentOf(current))
        {
            if (current is ButtonBase) return true;
        }
        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject element) =>
        element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    private static DeviceRowViewModel? RowOf(object source) => source switch
    {
        FrameworkElement element => element.DataContext as DeviceRowViewModel,
        FrameworkContentElement element => element.DataContext as DeviceRowViewModel,
        _ => null,
    };

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) Menus.Open(Section.BuildMenu(row), (UIElement)sender, PlacementMode.Bottom);
    }

    private void OnRowKeyDown(object sender, KeyEventArgs e)
    {
        // Only when the row itself has focus; its "..." button handles its own keys.
        if (e.OriginalSource is not Border { DataContext: DeviceRowViewModel row } element) return;

        var index = Section.Rows.IndexOf(row);
        var last = Section.Rows.Count - 1;
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Enter or Key.Space:
                Section.Activate(row);
                FocusDevice(row.Device.Id);
                break;
            case Key.Apps:
            case Key.F10 when shift:
                Menus.Open(Section.BuildMenu(row), element, PlacementMode.Bottom);
                break;
            case Key.Up when control && index > 0:
                Section.Move(index, index - 1);
                FocusDevice(row.Device.Id);
                break;
            case Key.Down when control && index < last:
                Section.Move(index, index + 2);
                FocusDevice(row.Device.Id);
                break;
            case Key.Up when !control && index > 0:
                RowElement(index - 1)?.Focus();
                break;
            case Key.Down when !control && index < last:
                RowElement(index + 1)?.Focus();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Refocuses a device after an action that may have rebuilt the rows.</summary>
    private void FocusDevice(string deviceId) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var index = Section.Rows.ToList().FindIndex(r => r.Device.Id == deviceId);
            if (index >= 0) RowElement(index)?.Focus();
        });

    private Border? RowElement(int index) =>
        Container(index) is { } container && VisualTreeHelper.GetChildrenCount(container) > 0
            ? VisualTreeHelper.GetChild(container, 0) as Border
            : null;

    private void OnRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        Menus.Open(Section.BuildMenu(row), (UIElement)sender, PlacementMode.MousePoint);
        e.Handled = true;
    }
}
