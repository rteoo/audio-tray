# Windows release preparation

Run from a clean `main` checkout with a tag matching the project version:

```powershell
.\prepare-release-windows.ps1 -Tag v1.0.0 -DryRun
.\prepare-release-windows.ps1 -Tag v1.0.0
```

Dry-run runs the core test gate and stops before packaging or promotion. The real
mode invokes `packaging\build.ps1` and validates the installer hash. It never tags,
pushes, publishes, or substitutes for the Linux/macOS/Store signing gates.
