# AGENTS.md

Fork of the macOS AudioPriorityBar, rebuilt as a native Windows 11 tray app. The Windows app is the
live product; the Swift sources under `AudioPriorityBar/` are the original macOS app, kept as the
behavioral reference.

## Stack

- C# on .NET 10 (`net10.0-windows`), WPF with the built-in Fluent theme (`Application.ThemeMode="System"`).
- No third-party runtime dependencies. Audio uses Windows Core Audio COM interop directly; default
  device switching uses the undocumented but stable `IPolicyConfig` interface.
- Tests: xUnit v3 over the Core library only.

## Layout

- `src/AudioPriorityTray.Core/` — UI-free logic: `AudioManager` (auto-switch policy, ported from the
  Swift `AudioManager`), `SettingsStore` (JSON in `%LOCALAPPDATA%\AudioPriorityTray\`),
  `WindowsAudioDeviceService` + `Interop/` (Core Audio).
- `src/AudioPriorityTray/` — WPF app: tray icon (raw `Shell_NotifyIcon`), Acrylic flyout, view models.
- `tests/AudioPriorityTray.Core.Tests/` — behavior tests against `FakeAudioDeviceService`.

## Commands

```bash
dotnet build AudioPriorityTray.slnx
dotnet test --project tests/AudioPriorityTray.Core.Tests
dotnet run --project src/AudioPriorityTray
dotnet publish src/AudioPriorityTray -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o publish
```

## Conventions

- `TreatWarningsAsErrors` is on for every project.
- Core Audio callbacks arrive on worker threads; `WindowsAudioDeviceService` coalesces them and
  posts to the UI `SynchronizationContext`. `AudioManager` is single-threaded.
- COM interface declarations must match SDK vtable order; only slots up to the last used method are declared.
- Colors come from Fluent theme resource keys via `DynamicResource`, never hard-coded, so light/dark
  and the accent color track Windows.
- Only one instance runs (`Local\AudioPriorityTray` mutex). Kill the running tray app before `dotnet run`.
