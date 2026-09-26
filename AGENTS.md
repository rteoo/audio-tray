# AGENTS.md

Native Windows 11 tray app that keeps the highest-priority connected audio device as the Windows
default. Rebuilt from the macOS AudioPriorityBar (github.com/tobi/AudioPriorityBar); the Swift
sources were removed from this repo, and its git history holds them if the original behavior
needs checking.

## Stack

- C# on .NET 10 (`net10.0-windows`), WPF with the built-in Fluent theme (`Application.ThemeMode="System"`).
- No third-party runtime dependencies. Audio uses Windows Core Audio COM interop directly; default
  device switching uses the undocumented but stable `IPolicyConfig` interface.
- Tests: xUnit v3 over the Core library only.

## Layout

- `src/AudioPriorityTray.Core/` — UI-free logic: `AudioManager` (auto-switch policy), `SettingsStore`
  (JSON in `%LOCALAPPDATA%\AudioPriorityTray\`), `WindowsAudioDeviceService` + `Interop/` (Core Audio).
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
- UI follows Windows Design System 1.0.0; `DESIGN.md` records the mapping and deliberate
  exceptions. Update it with any new role, deviation, or verified gate.
- Colors come from Fluent theme resource keys via `DynamicResource`, never hard-coded, so light/dark
  and the accent color track Windows. A brush with a `DynamicResource` color can't be used from
  template triggers (it throws at runtime); use `SystemColors.*BrushKey` or an inline element.
- Only one instance runs (`Local\AudioPriorityTray` mutex). Kill the running tray app before `dotnet run`.
