# AI Usage Widget

A floating desktop widget for checking Codex and Grok usage on Windows and macOS, built with C#/.NET and Avalonia.

Current release: **[v1.5.1](https://github.com/wernerong/ai-usage-widget/tree/v1.5.1)**. See [CHANGELOG.md](CHANGELOG.md) for history. The installed version appears at the bottom of the widget or tray/menu bar menu.

## Features

- Select Codex, Grok, or both; choices persist across restarts.
- Compact round badges or detailed cards, sized to the selected providers.
- Codex five-hour and weekly allowances, extra credits, and available free resets.
- Grok weekly usage and prepaid credit balance.
- Percentage remaining or percentage used display, with reset countdowns and exact times in tooltips.
- Independent provider refreshes every minute; stale readings remain visible when requests fail.
- Drag to move, remembered position, always on top, and adjustable opacity.
- Windows tray and macOS menu bar controls; both installers enable launch at login, with an option to disable it.
- Application icons embedded in the Windows executable and macOS app bundle.

Claude is not yet supported. The provider registry allows future integrations without duplicating the application UI.

## Requirements

- Windows 10/11 (x64), or macOS 13 or later (Apple Silicon or Intel).
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build. Self-contained packages include the runtime.
- For Codex: Codex CLI installed and signed in with a ChatGPT account exposing account rate limits.
- For Grok: Grok CLI / Build signed in with OAuth (`grok login`).

Only selected providers need logins. A missing login for one provider does not prevent another from updating. Selecting a provider enables monitoring; it does not purchase a subscription or sign you in.

The bundle permits macOS 13, with startup reported working on 13.5. This compatibility setting does not extend Microsoft's official .NET 10 operating-system support policy, documented [here](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).

## Choose your subscriptions

Right-click the widget or use its tray/menu bar menu, then open **Subscriptions**. Select **Codex**, **Grok**, or both. Both start enabled for compatibility, and at least one stays selected. Disabled providers stop refreshing and disappear from summaries, tooltips, usage links, diagnostics, and health snapshots.

Open **Percentage display** and choose **Percentage remaining** or **Percentage used**. A checkmark identifies the current choice, and the widget updates immediately. Preferences are preserved across restarts and Windows upgrades from v1.4.0.

## Windows build and install

Run in PowerShell:

```powershell
git clone https://github.com/wernerong/ai-usage-widget.git
cd ai-usage-widget
dotnet publish source/UsageWidget.csproj -c Release -r win-x64 --self-contained true -o app
.\Install.ps1
```

The installer copies the build to `%LOCALAPPDATA%\Programs\AIUsageWidget`, creates a Start menu shortcut, enables **Start with Windows**, and launches the widget. Exit an existing instance before reinstalling. Run `Uninstall.ps1` to uninstall; logins and preferences are preserved.

For portable use, run `app\AIUsageWidget.exe`. Framework-dependent builds can use `--self-contained false` and require the .NET 10 Runtime; the Windows Desktop Runtime is no longer required.

## macOS build and install

Run on a Mac with the .NET 10 SDK:

```bash
git clone https://github.com/wernerong/ai-usage-widget.git
cd ai-usage-widget
bash scripts/build-macos.sh
```

The script chooses the current CPU architecture. To choose explicitly, pass `osx-arm64` for Apple Silicon or `osx-x64` for Intel. It builds a self-contained `.app` bundle and a versioned ZIP under `dist/`, including `Install.command` and `Uninstall.command`.

Extract the ZIP and run `Install.command`. This installs to `~/Applications/AI Usage Widget.app`, enables **Start at login**, and opens the widget. Startup uses `~/Library/LaunchAgents/com.wernerong.ai-usage-widget.plist` to launch automatically at subsequent logins. Disable **Start at login** from the menu to remove this file.

To upgrade, exit the widget, move the previous `.app` to Trash, and install at the same path. Preferences persist. To uninstall, exit and run `Uninstall.command`; it removes the login agent and moves the app to Trash.

Mac packages use an ad-hoc signature for local testing. They are not Developer ID-signed or notarized. Public distribution requires signing and notarization on a Mac; this repository does not include signing credentials. The latest menu fix still needs native Mac verification; Windows and automated menu checks pass.

## Codex discovery on macOS

Finder and login-launched applications may not inherit your terminal's PATH. The widget also checks `/opt/homebrew/bin`, `/usr/local/bin`, `~/.local/bin`, and the Codex app's Resources folder in `/Applications` or `~/Applications`. Existing Windows executable discovery is retained.

For a nonstandard installation, set `AI_USAGE_WIDGET_CODEX_PATH` to the full executable path in the widget's launch environment. For example, launch a local Mac build from Terminal:

```bash
AI_USAGE_WIDGET_CODEX_PATH="/your/path/to/codex" "$HOME/Applications/AI Usage Widget.app/Contents/MacOS/AIUsageWidget"
```

A terminal-only environment variable does not persist into Finder or login launches. Install the CLI in a searched location for those methods, or configure the launch environment accordingly. Node-based Codex wrappers need Node beside the wrapper or in the launch PATH; Homebrew locations are added automatically on macOS.

## Controls

| Action | Control |
| --- | --- |
| Move | Drag a badge or detailed header |
| Select providers | Menu → Subscriptions |
| Choose used or remaining | Menu → Percentage display |
| Switch layout | Menu → Layout |
| Expand badges | Double-click a badge |
| See readings and reset dates | Hover a provider |
| Refresh | F5, refresh button, or menu |
| Hide | Escape, minimize button, or Show / hide widget |
| Restore | Tray icon, menu bar Show / hide widget, or launch again |
| Settings / exit | Right-click, ⋮ button, or Shift+F10 |

Free-reset counts are display-only: the widget never redeems them.

## Data sources and privacy

**Codex:** starts a hidden local `codex app-server` process and calls `account/rateLimits/read`. Five-hour and weekly windows are selected by duration; missing values remain unavailable.

**Grok:** reads the existing OAuth login from `$GROK_HOME/auth.json`, or `~/.grok/auth.json`, and requests `https://cli-chat-proxy.grok.com/v1/billing?format=credits`. Prepaid balances convert USD cents to dollars. This internal endpoint may change. A valid unified weekly response with an omitted percentage follows the CLI's zero-used behavior and explains it in the tooltip. Monthly billing is never substituted for weekly usage.

Credentials are used only with the corresponding provider. Refreshed Grok credentials remain in memory; the widget does not overwrite CLI login files. It does not read browser cookies, send model prompts, purchase credits, redeem resets, change billing settings, or send telemetry.

Preferences and a credential-free health snapshot are stored in `%LOCALAPPDATA%\AIUsageWidget` on Windows and `~/Library/Application Support/AIUsageWidget` on macOS. Snapshots include version, platform, selected providers, safe errors, and window state. These local files are not committed.

## Validation and packaging

```bash
dotnet restore tests/UsageWidget.UiChecks.csproj --locked-mode
dotnet build source/UsageWidget.csproj -c Release
dotnet source/bin/Release/net10.0/AIUsageWidget.dll --self-test
dotnet run --project tests/UsageWidget.UiChecks.csproj -c Release
```

Self-tests cover parsing, legacy preferences, provider cancellation, stale data, and platform helpers. UI checks use the real Skia renderer on a headless desktop to exercise menus, sizing, hit testing, percentage choices, transparency, and stale states without provider logins or modifying user preferences.

Additional switches:

- `--version`: prints the version (invoke the DLL with `dotnet` when using a console).
- `--diagnose`: fetches selected providers and writes `diagnostics.json` beside the executable.
- `--render --compact` or `--render --cards`: renders live readings to `widget-preview.png` and exits.
- `--render-check`: exercises native transitions using synthetic data and writes layout PNGs and `render-checks.txt` beside the executable. User preferences are preserved.

Exit the running widget before render/diagnostic checks. Render outputs require a writable executable directory. Live diagnostics and screenshots may contain personal usage information; generated files are excluded from Git. A second normal launch restores the existing window instead of starting another monitor.

GitHub Actions builds and runs parsing and headless UI checks on Windows, Intel macOS, and Apple Silicon macOS. It packages versioned Windows and Mac ZIPs as workflow artifacts. Native rendering, authentication, installation, and login behavior are separate manual smoke checks. Workflow artifacts do not automatically create a GitHub release.

## Adding a provider

Definitions in `source/Widget.cs` supply a stable settings ID, name, usage URL, description, accent, quota labels, free-reset capability, default selection, and cancellable reader returning `Reading`. Add its logo to `source/Assets`. New integrations should default to opt-in. Compact badges support one or two quotas; other shapes require a matching layout.

`UsageMonitor.cs` manages selection and refresh lifecycles, `ProviderSurface.cs` draws the shared UI, and `PlatformServices.cs` contains platform-specific startup and instance activation. Dependencies are pinned in the project and lockfiles.

## Versioning

`<Version>` in `source/UsageWidget.csproj` is the source of truth for the application, assembly, and generated Mac bundle version. Use semantic versioning: major for breaking changes, minor for features, patch for fixes. Update this README and `CHANGELOG.md` with each version. Released versions receive an annotated Git tag named `vMAJOR.MINOR.PATCH`; development versions are not automatically tagged or published.

## Credits

Platform icons come from [Lobe Icons](https://github.com/lobehub/lobe-icons); their license is included in [source/Assets/LICENSE-lobe-icons.txt](source/Assets/LICENSE-lobe-icons.txt). Platform names and marks belong to their owners. The application uses [Avalonia](https://avaloniaui.net/) for Windows and macOS rendering.

References: [Codex app-server](https://learn.chatgpt.com/docs/app-server), [Grok billing implementation](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-shell/src/extensions/billing.rs).

This project is not affiliated with or endorsed by OpenAI or xAI.
