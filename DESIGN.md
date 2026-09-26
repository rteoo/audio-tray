# Design

Audio Priority adopts **Windows Design System 1.0.0** (2026-09-25). The system's `DESIGN.md`,
`windows.md`, and `components.md` are the contract; this file records how this app applies it and
where it deliberately deviates.

## Adoption record

| Field | Decision |
| --- | --- |
| Adopted version | Windows Design System 1.0.0 |
| Product ownership | Personal Windows utility; no brand overlay. The accent is the user's Windows accent. |
| Primary workflow | From the tray, see which devices are active and change mode or priority in one or two clicks. |
| Platform lane | WPF on .NET 10 with the built-in Fluent theme (`ThemeMode="System"`). See exception 1. |
| Theme / density | Follows the Windows theme live; Comfortable density only. |
| Support matrix | Tested on Windows 11 build 26200, x64, 100% scaling, dark and light themes. Everything else is untested. |

## How the tokens map

| Role | Implementation |
| --- | --- |
| Colors | Fluent theme resources via `DynamicResource` (`TextFillColor*`, `SubtleFillColor*`, `ControlFillColor*`, `AccentFillColor*`); no hard-coded colors. |
| Accent fills | `AccentFillColorDefaultBrush`: Windows' own rule, the accent's *Light 2* shade in dark mode and *Dark 1* in light mode, with on-accent text. |
| Selection | Accent pill plus a backplate of `SystemColors.AccentColorBrush` at 24% (32% on hover) and a semibold name, so it never relies on color alone. Measured with an orange accent: 9.7:1 text contrast in dark mode, 8.4:1 in light mode. |
| Surface | Transient Acrylic backdrop (DWM) under a 90% tint of `SolidBackgroundFillColorBaseBrush`. Measured `#26`–`#2B` in dark mode and `#E3`–`#E8` in light mode. See exception 2. |
| Type | Segoe UI Variable Text: Body 14, Body strong 14/600 for section titles and the active device, Caption 12 for metadata. |
| Space / geometry | 4px rhythm (2, 4, 8, 12, 16); 4px control radius, 8px overlay radius (DWM rounded corners). |
| Density | Comfortable: 44px rows, 40px icon targets. |
| Focus | 2px `FocusStrokeColorOuterBrush` ring with a 1px gap, shown for keyboard focus only. |
| Motion | 300ms spatial reveal with an 8–12px offset on `cubic-bezier(.2,0,0,1)`; 100ms fade. Skipped when Windows animation effects are off. |

## Keyboard contract

- Tab reaches the mode tiles, volume slider, each device row, each row's "…" button, and the footer.
- On a row: Enter or Space activates it, Up/Down moves between rows, Ctrl+Up/Down reorders it (the
  non-drag route for drag-to-reorder), and Shift+F10 or the menu key opens its actions.
- Escape closes the flyout.

## Exceptions

1. **WPF, not WinUI 3.** The system recommends WinUI 3 for new Windows-only apps. WinUI 3 has no
   notification-area API and adds Windows App SDK packaging for an unpackaged utility. WPF's Fluent
   theme supplies native theme resources and control templates. Review this if the app grows
   windows beyond the flyout.
2. **Tinted Acrylic.** Raw DWM transient Acrylic measured `#545454` over bright windows, far from
   the dark canvas. The tint follows Windows' own flyout recipe (a high-luminosity layer over the
   blur), so the flyout reads as the dark or light canvas and keeps a hint of translucency.
3. **No in-app Light/Dark/System choice.** The system asks for an Appearance setting. The flyout is
   a transient shell surface that follows Windows like the system flyouts do, and the app has no
   settings window to host the choice.
4. **Fixed 360px flyout.** The layout breakpoints don't apply; it's a flyout, not a resizable window.
5. **Row selection state reaches UI Automation as text.** Rows expose name and `ItemStatus`
   ("Priority 1, Active, Muted"), not the SelectionItem pattern. Review after a Narrator pass.

## Unverified gates

The following haven't been exercised yet: Narrator, Windows contrast themes, 125–200% scaling and
mixed-DPI monitors, a keyboard-only pass on the live flyout, and switching reduced motion or the
theme while the flyout is open.
