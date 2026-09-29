# AGENTS.md

Follow the active runtime's global `AGENTS.md` and `SOUL.md`. This file
adds project-specific facts and commands; it cannot weaken global approval
or privacy rules.

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

## Packaging

`packaging/build.ps1 -Runtime win-x64|win-arm64 [-Version x.y.z] [-Msix ...]` publishes self-contained
into `artifacts/publish/`, compiles `packaging/AudioPriorityTray.iss` (Inno Setup, per-user, fixed
AppId) and optionally packs `packaging/AppxManifest.xml` + `packaging/Assets/` with `makeappx`.

- The installer's startup task writes the same `HKCU\...\Run\AudioPriorityTray` value as the app's
  toggle, and uninstall always removes it. The AppId GUID must never change.
- Store rules: the MSIX version's fourth field stays `0` and versions must increase; every
  DisplayName must equal the Partner Center reserved name; `runFullTrust` needs a justification in
  the submission; the MSIX is uploaded unsigned. Packaged builds can't use the Run key
  (`LaunchAtLogin.IsManagedByWindows`); the manifest's opt-in `startupTask` is used instead.

## CI and releases

`.github/workflows/build.yml` builds and tests on every PR and push. Pushes to `main` replace the
rolling `nightly` pre-release; pushing a `v*` tag publishes a versioned release (the tag sets the
version). Assets: per-user installers and portable framework-dependent exes for win-x64 and
win-arm64. The Store MSIX is a build artifact only, produced when the `AUDIOPRIORITY_MSIX_*`
repository variables are set.

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

## Public repository boundary

Before committing, inspect staged code, screenshots, build logs, and metadata for device names, host details, credentials, and personal data. Before an authorized push or release, inspect outgoing commits, refs, and artifacts; CI publishing triggers do not grant publication authority.
