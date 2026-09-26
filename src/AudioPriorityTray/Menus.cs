using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AudioPriorityTray.ViewModels;

namespace AudioPriorityTray;

/// <summary>Turns <see cref="MenuEntry"/> lists into Fluent-styled context menus.</summary>
internal static class Menus
{
    public static ContextMenu Create(IEnumerable<MenuEntry> entries)
    {
        var menu = new ContextMenu();
        foreach (var entry in TrimSeparators(entries))
        {
            if (entry.IsSeparator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var item = new MenuItem { Header = entry.Header, IsChecked = entry.IsChecked, InputGestureText = entry.Hint ?? "" };
            if (entry.Glyph is { } glyph)
                item.Icon = new TextBlock { Text = glyph, FontFamily = Glyphs.FontFamily, FontSize = 16 };
            if (entry.Execute is { } execute)
                item.Click += (_, _) => execute();
            menu.Items.Add(item);
        }
        return menu;
    }

    public static ContextMenu Open(IEnumerable<MenuEntry> entries, UIElement? target, PlacementMode placement)
    {
        var menu = Create(entries);
        menu.PlacementTarget = target;
        menu.Placement = placement;
        menu.IsOpen = true;
        return menu;
    }

    /// <summary>Drops leading, trailing and doubled separators left by conditional entries.</summary>
    private static IEnumerable<MenuEntry> TrimSeparators(IEnumerable<MenuEntry> entries)
    {
        var pendingSeparator = false;
        var any = false;
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                pendingSeparator = any;
                continue;
            }
            if (pendingSeparator) yield return MenuEntry.Separator;
            pendingSeparator = false;
            any = true;
            yield return entry;
        }
    }
}
