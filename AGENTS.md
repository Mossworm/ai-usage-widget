# Build, verification, and installation

- Use the root `build-install.ps1` as the standard workflow for building, checking, packaging, and installing this project. Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-install.ps1` after changes when installation is requested or part of the task. Do not replace it with a separate sequence of manual publish, packaging, or registration commands.
- Use the root `uninstall-build-install.ps1` when a clean reinstall is requested: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall-build-install.ps1`. It requires Developer Mode, removes this checkout's development registration (app data preserved unless `-RemoveAppData`), moves the leftover `artifacts/package` aside, and then runs `build-install.ps1`. It only forwards `-Configuration`; for build-only work call `build-install.ps1` directly.
- Use `-BuildOnly` when only build verification is requested, installation is explicitly excluded, or the environment cannot support installation. Report any installation that remains unverified or blocked.
- The default configuration is Release; use `-Configuration Debug` only when needed.
- The script runs the offline checks, publishes both apps, renders sample previews, validates MSIX packaging, registers the current user's development package, and verifies COM provider activation. Live account checks and interactive widget-panel checks are separate and must not be described as covered by this script.
- Keep `packaging/Assets` and the script sufficient for a clean checkout. Do not depend on old files in `artifacts` to build a package.
- Preserve existing user settings, signed installations, other checkouts, and backup files. If the workflow needs changes, update the script and README together.
