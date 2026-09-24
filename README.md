# AI Usage Widget

[한국어](README_kr.md)

A Windows 11 widget that shows your **Codex** and **Claude Code** subscription usage in the Widgets board (Win + W). It follows the Windows light and dark themes.

| Light | Dark |
| --- | --- |
| ![Light theme](assets/Light.png) | ![Dark theme](assets/Dark.png) |

- 5-hour and weekly limits with reset times and progress bars
- An extra per-model weekly limit for Claude Code (Fable by default)
- A toggle for each service in the widget's **Customize widget** menu
- No sign-in screen of its own. The widget reuses the sign-in of the Codex and Claude Code CLIs already on your PC.

## Requirements

- Windows 11 22H2 or later, x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/) (provides `makeappx.exe` and `makepri.exe`)
- Windows Web Experience Pack (the Widgets board; installed by default on most PCs)
- **Developer Mode** turned on: Settings → System → For developers → Developer Mode
- Windows PowerShell 5.1 (built into Windows)
- An internet connection for the first build (NuGet packages)

At least one of these CLIs, installed and signed in:

- **Codex**: `npm install -g @openai/codex`, then `codex login` with your ChatGPT account
- **Claude Code**: install [Claude Code](https://code.claude.com/docs) and sign in once by running `claude`

## Install

1. Clone the repository.

   ```powershell
   git clone https://github.com/Mossworm/ai-usage-widget.git
   cd ai-usage-widget
   ```

2. Build and install. The script builds, runs the offline checks, packages an MSIX, and registers the widget for the current user.

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-install.ps1
   ```

3. Open the Widgets board with **Win + W**, select **Add widgets**, and pin **AI Usage**.

4. To choose which services appear, open the widget's **⋯** menu and select **Customize widget**.

> [!IMPORTANT]
> The widget runs from `artifacts/package` inside the repository. Don't move or delete that folder or the repository after installing.

### Update

Pull the latest changes and run the same command again. Your settings are kept.

```powershell
git pull
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-install.ps1
```

### Clean reinstall

Removes the current registration and then runs `build-install.ps1`. App data is kept unless you add `-RemoveAppData`.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall-build-install.ps1
```

### Uninstall

```powershell
Get-AppxPackage -Name Mossworm.AIUsageWidget | Remove-AppxPackage
```

To remove settings too, delete `%LOCALAPPDATA%\AiUsageWidget`.

### Build options

| Option | Effect |
| --- | --- |
| `-BuildOnly` | Build, check, and package without installing. Doesn't need Developer Mode. |
| `-Msix` | Same as `-BuildOnly`. |
| `-Configuration Debug` | Debug build instead of Release. |

The MSIX is written to `artifacts/msix/` and copied to `artifacts/AiUsageWidget.msix`. It isn't signed, so it can't be installed on other PCs by double-clicking. Use `build-install.ps1` on each PC instead.

## How it gets usage

**Codex.** The widget starts the local `codex app-server` and asks it for the account and rate limits. It doesn't run prompts or call models, and it doesn't read `auth.json`. If more than one `codex.exe` is installed, the most recently updated one is used. To pin a specific binary, set `CODEX_EXECUTABLE` to its full path.

**Claude Code.** The widget reads the access token from `%USERPROFILE%\.claude\.credentials.json` (or `CLAUDE_CONFIG_DIR`) and requests `https://api.anthropic.com/api/oauth/usage`. It only reads the file and never refreshes the token, so it can't sign you out of Claude Code. If the token has expired, the widget asks you to open Claude Code, which refreshes it.

> [!WARNING]
> `/api/oauth/usage` is an undocumented endpoint used by Claude Code, not a public Anthropic API. It can change or stop working at any time.

Usage figures are kept in memory only. The widget has no analytics, telemetry, or developer server. See the [privacy policy](docs/privacy-policy.md).

### Environment variables

| Variable | Default | Purpose |
| --- | --- | --- |
| `CODEX_EXECUTABLE` | (auto-detect) | Full path to the `codex.exe` to use |
| `CLAUDE_CONFIG_DIR` | `%USERPROFILE%\.claude` | Claude Code config folder |
| `AIUSAGE_CLAUDE_MODEL_WINDOWS` | `fable` | Per-model weekly limits to show, comma-separated (`fable,opus`). Empty shows none. |
| `AIUSAGE_CLAUDE_USER_AGENT` | `claude-code/0.2.29` | User-Agent sent with the Claude usage request |

## Troubleshooting

- **"Sign in" or "expired" message.** Run `codex login`, or open Claude Code once.
- **AI Usage isn't in Add widgets.** Check that Developer Mode is on and that `build-install.ps1` finished without errors, then close and reopen the Widgets board.
- **Customize widget doesn't open the settings.** Run `uninstall-build-install.ps1` to reinstall.
- **Check the connection outside the widget.** These print only the usage figures and reset times:

  ```powershell
  dotnet run --project tests/AiUsage.Checks -- --live
  dotnet run --project tests/AiUsage.Checks -- --live-claude
  ```

## Desktop preview

`artifacts/package/Desktop/AiUsage.Desktop.exe` opens the same content in a normal window. Add `--sample` to see fixed sample data without signing in.

## References

- [Implement a widget provider (Microsoft)](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/implement-widget-provider-cs)
- [Codex App Server](https://learn.chatgpt.com/docs/app-server)
- The Codex RPC approach is based on [spourdei/codex-usage-widget](https://github.com/spourdei/codex-usage-widget) and [ZeroP27/codex-usage](https://github.com/ZeroP27/codex-usage).
