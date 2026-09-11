# AI Usage Widget

A lightweight Windows desktop widget for checking Codex and Grok usage without opening their dashboards.

## Features

- Select subscriptions from the menu: show Codex, Grok, or both.
- Two layouts: compact round badges and detailed cards.
- Codex five-hour and weekly allowances, extra credits, and available free resets.
- Grok weekly usage and prepaid credit balance.
- Percentage remaining or percentage used, with reset countdowns and exact times on hover.
- Automatic refresh every minute, with stale readings clearly marked if a request fails.
- Draggable, remembered position; always-on-top, opacity, and Windows startup options.
- Tray icon for hiding and restoring the widget.
- Smooth transparent badge edges and embedded platform icons.

## Requirements

- Windows 10 or Windows 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build. The default build requires the .NET 10 Windows Desktop Runtime to run.
- Codex installed and signed in with a ChatGPT account that exposes account rate limits.
- Grok CLI / Build signed in using OAuth (`grok login`) for Grok usage.

Choose **Subscriptions** in the widget or tray menu to toggle providers. Both start enabled for compatibility, and at least one stays selected. Your selection is remembered and both layouts resize automatically. Disabled providers stop refreshing and disappear from tooltips, tray summaries, usage links, and health snapshots.

Each provider refreshes independently. A missing login for one provider does not prevent the other from updating.

## Build and install

Clone the repository, then run these commands in PowerShell:

```powershell
git clone https://github.com/wernerong/ai-usage-widget.git
cd ai-usage-widget
dotnet publish source/UsageWidget.csproj -c Release -r win-x64 --self-contained false -o app
.\Install.ps1
```

The installer copies the build to `%LOCALAPPDATA%\Programs\AIUsageWidget`, creates a Start menu shortcut, enables startup for the current user, and launches the widget. Exit an existing instance from its tray menu before reinstalling. Disable **Start with Windows** from the widget menu if desired.

For a portable build, run `app\AIUsageWidget.exe` directly. To package the runtime with the application, publish with `--self-contained true` instead.

To uninstall, exit the widget and run `Uninstall.ps1`. CLI logins and widget preferences are preserved.

## Controls

| Action | Control |
| --- | --- |
| Move | Drag a round badge or the detailed header |
| Select providers | Right-click → Subscriptions |
| Switch layout | Right-click → Layout |
| Expand badges | Double-click a badge |
| See all readings and reset dates | Hover the provider |
| Refresh | F5, the refresh button, or the tray menu |
| Hide | Escape or the minimize button |
| Restore | Click the tray icon or launch from Start |
| Settings / exit | Right-click, the ⋮ button, or Shift+F10 |

The layout, position, opacity, and display preferences persist between launches. Free-reset counts are display-only: the widget does not redeem them.

## Data sources and privacy

**Codex:** the widget starts a hidden local `codex app-server` process and calls the documented `account/rateLimits/read` interface. It selects the five-hour and weekly windows by their durations and reads the authoritative free-reset count. Missing values remain unavailable.

**Grok:** the widget reads the existing CLI OAuth login from `%GROK_HOME%\auth.json`, or `%USERPROFILE%\.grok\auth.json`, and requests `https://cli-chat-proxy.grok.com/v1/billing?format=credits`. This is the billing endpoint used by Grok Build. The unified account pool covers Grok products on that account. Prepaid balances are converted from USD cents to dollars.

Grok's internal endpoint may change. For a valid unified weekly response with an omitted usage percentage, the widget follows the official CLI's zero-used behavior and explains that in the tooltip. Monthly billing figures are never substituted for weekly usage.

Existing credentials are used only with the corresponding provider. Refreshed Grok credentials remain in memory; the widget does not overwrite CLI login files. It does not read browser cookies, send model prompts, purchase credits, redeem resets, change billing settings, or send telemetry.

Preferences and a local health snapshot are stored in `%LOCALAPPDATA%\AIUsageWidget`. The health snapshot contains percentages, balances, safe connection errors, and window state. These local files are not part of the repository.

## Validation

```powershell
dotnet build source/UsageWidget.csproj -c Release
dotnet source/bin/Release/net10.0-windows/AIUsageWidget.dll --self-test
Get-Content source/bin/Release/net10.0-windows/checks.txt
```

Additional developer switches:

- `--diagnose`: fetches live data and writes `diagnostics.json` beside the executable.
- `--render --compact` or `--render --cards`: renders the selected layout after fetching live data, then exits.
- `--render-check --compact`: checks the native layout transitions, opacity, and transparent edges, then exits.

Rendering checks require an interactive Windows desktop and affect the saved layout. Exit any running instance first. Diagnostic output and rendered screenshots can contain personal usage information; they are excluded from Git.

## Credits

Platform icons come from [Lobe Icons](https://github.com/lobehub/lobe-icons). Their license is included in [source/Assets/LICENSE-lobe-icons.txt](source/Assets/LICENSE-lobe-icons.txt). Platform names and marks belong to their respective owners.

References: [Codex app-server](https://learn.chatgpt.com/docs/app-server), [Grok billing implementation](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-shell/src/extensions/billing.rs).

This project is not affiliated with or endorsed by OpenAI or xAI.

## Adding a provider

Provider definitions in `source/Program.cs` supply a stable settings ID, display name, usage URL, description, accent, quota labels, free-reset capability, default selection, and a cancellable reader returning `Reading`. Add its embedded logo in `source/Assets`. Menus, selection, refresh scheduling, cards, and tooltips use this registry. New integrations should default to opt-in. Compact badges currently support one or two quotas; other quota shapes need a corresponding badge layout. Claude is not yet supported.
