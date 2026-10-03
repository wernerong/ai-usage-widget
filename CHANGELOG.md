# Changelog

User-visible changes are recorded here, newest first. Versions follow semantic versioning.

## [Unreleased]

## [1.13.1] - 2026-10-03

- Replace the ChatGPT/OpenAI knot used for Codex with the dedicated Codex terminal mark in every layout, island style, and theme. Keep native-scale vector rendering for sharp Windows and Mac icons.
- Refresh the legacy Codex PNG and layout screenshots, document the pinned artwork source, and add regression checks for the terminal symbol and transparent counters.
- Provider readers, authentication, preferences, the widget's own app icon, and automatic-update behaviour are unchanged.

## [1.13.0] - 2026-10-03

- Add Adaptive Island: compact at rest, with only the hovered provider expanding to show its name, quotas, and nearest reset. Collapse after a 220 ms exit delay; keep dragging independent of hover changes.
- Add Spotlight Island: one provider per page in a slim horizontal island, with mouse-wheel, pager, and menu navigation.
- Add the separate, persisted Island Style setting: Continuous or Provider Pills. Both styles work with Adaptive Island, Spotlight Island, Island Bar, and Compact Island; existing users retain their layout and default to Continuous.
- Share island geometry between rendering, provider hit targets, paging, and dynamic sizing. Keep hover resizing centre-anchored and screen-clamped without changing the saved resting position; preserve Windows taskbar placement and macOS usable-desktop bounds.
- Preserve sharp provider logos, full tooltips, used/remaining display, consumption-based alert colours, and explicit missing/unlimited/stale states. Provider readers, authentication, and update behaviour are unchanged.
- Add island-specific regression checks and light/dark render proofs covering both styles, deterministic hover, delayed collapse, dragging, edge anchoring, paging, preference migration, and mixed display scales.

## [1.12.0] - 2026-10-02

- Add Compact List, Tile Grid, Usage Bars, Focus Mode, and Status Rail alongside all four existing layouts.
- Show six providers per page in list/grid/bars, one in Focus, and eight in Status Rail. Existing layouts retain three-provider pages.
- Replace layout flags with a single named preference and migrate existing saved layouts without changing positions, subscriptions, theme, opacity, or updater preferences.
- Preserve the viewed provider when switching between density levels, and keep paging valid when subscriptions change.
- Reuse provider details, sharp logos, used/remaining percentages, usage-based severity, missing/unlimited states, and light/dark themes. New layouts support drag, refresh, settings, and previous/next controls.
- Add real-render checks for all nine layouts, 1–13 selected providers, mixed display scales, preference migration, pointer navigation, and stale/loading/unknown quota states. Publish light/dark layout proofs as build artifacts.

Provider authentication, usage readers, and automatic-update behaviour are unchanged.

## [1.11.2] - 2026-09-28

- Fix automatic updates for login-started Macs: allow the updater helper to survive the widget exiting instead of being killed with its launchd process group.
- Migrate existing startup configuration on normal installed-app launches while preserving custom settings and leaving startup disabled if it was disabled. The migrated policy takes effect when launchd next loads the job.
- Exercise the actual macOS launch-agent process tree in packaged upgrade tests, in addition to corrupt-package rejection and restart verification on all three platforms.
- Preserve macOS signatures through automatic updates by sealing managed .NET files under Resources, with relative links for the runtime. Verify the signature after an actual upgrade and after a transfer without extended attributes.
- Includes all sharp icon improvements from v1.11.1. Macs still using v1.11.0/v1.11.1's old login agent should reopen the widget from Finder once to update; the repaired agent then takes effect at the next login.

## [1.11.1] - 2026-09-28

- Render provider logos from vector paths at the display's native scale, with pixel-aligned placement in every layout and both themes. Improve filtering for the existing Grok Bot raster artwork.
- Replace the 64px application icon with a vector master, a complete macOS Retina iconset through 1024px, and nine Windows ICO sizes from 16px to 256px.
- Give tray and menu-bar icons dedicated, optically adjusted small-size artwork instead of rescaling the application image.
- Add reproducible icon generation, multi-resolution asset validation, and light/dark visual proofs. Retain automatic update integrity and restart checks on Windows, Intel Mac, and Apple Silicon Mac.

## [1.11.0] - 2026-09-28

- Enable automatic stable-release updates from GitHub on Windows and both Mac architectures after one installer upgrade.
- Verify downloads and restart quietly while preserving preferences and provider credentials.
- Add update status, manual checks, and an automatic-update opt-out to the widget menu.
- Test corrupt-download rejection and an actual packaged update/restart before publishing all platform feeds together.

## [1.10.0] - 2026-09-28

### Added

- Add opt-in Claude Code, Cursor CLI, GitHub Copilot, and Gemini CLI / Code Assist quota readers. Reuse supported local logins; API billing and consumer web-chat quotas remain separate.
- Add Kimi Code, Z.ai / Zhipu GLM Coding Plan, and international / China MiniMax Token Plan connections. Store entered keys in Windows Credential Manager or macOS Keychain.
- Connect Antigravity's documented CLI status-line quota feed with a settings backup; preserve existing custom status lines and store only quota data.
- Page all layouts in groups of three providers, with mouse-wheel navigation, a next-page control, and menu navigation. Keep existing provider selections unchanged.
- Use whole percentages, explicit unlimited states, authoritative provider resets, and HTTP rate-limit backoff. Preserve missing values as unavailable.

### Validation limits

- New providers are tested against synthetic contract fixtures and native credential stores; paid-account end-to-end checks require accounts not available to the maintainer. Internal vendor endpoints can change.
- Antigravity shows the latest CLI snapshot and marks old data stale. MiniMax requires explicit remaining-percent fields; ambiguous older count-only responses are rejected.

## [1.9.0] - 2026-09-28

### Added

- Support Grok Bot desktop logins on macOS through its existing Safe Storage Keychain item, with credentials decrypted only in memory.
- Show the same weekly percentage, reset time, and on-demand spending in all layouts on Windows and Mac. Explain missing, locked, or denied Keychain access.
- Validate the macOS encrypted storage format and exercise an isolated synthetic Keychain on Apple Silicon and Intel build runners.

## [1.8.1] - 2026-09-28

### Added

- Package Windows as a per-user Setup executable with shortcuts, an optional startup setting, and standard uninstall support.
- Package Apple Silicon and Intel Macs as native per-user installers with a bundled runtime.
- Build, smoke-test, and publish installers with checksums on version tags. Preserve preferences and existing provider logins during upgrades.

### Distribution

- Installers are currently unsigned and macOS packages are not notarized. Operating-system verification prompts may appear.


## [1.8.0] - 2026-09-28

### Changed

- Display whole-number percentages throughout the widget and preserve its position when switching to the island bar.

### Added

- Add Compact island with provider logos and whole-number usage values, full hover details, and an optional Codex 5-hour value shown only when available. Keep the detailed island option and persist the compact selection.

- Add opt-in Grok Bot monitoring on Windows using the active desktop login: weekly used/remaining percentage, reset time, and on-demand spending. Credentials stay in the app's encrypted storage and are only decrypted in memory.
- Support Grok Bot in badge, card, and island layouts, including dark mode. Size island labels to avoid overlap and shorten the card footer when three providers are selected.
- Explain expired login and unavailable usage without displaying fabricated balances. Grok Bot token renewal remains owned by its desktop app.

## [1.7.0] - 2026-09-13

### Added

- Add a persistent Dark mode toggle for island, badge, and card layouts, with contrasting provider logos, usage colors, widget menus, and tooltips. Native macOS menus retain the system appearance.

### Validation

- Pass 137 UI checks covering theme switching, persistence, layout stability, transparency, stale readings, and percentage selection. Windows and macOS builds succeed; native Mac dark-mode testing remains pending.

## [1.6.0] - 2026-09-13

### Fixed

- Constrain dragging to connected displays on Windows and macOS, permit transfers between monitors, account for display scaling and gaps, and recover the widget when a display is disconnected.
- Keep pinned Windows islands above overlapping taskbars when switching apps or when Explorer changes window order, without taking keyboard focus. Stop enforcing this when hidden or Always on top is disabled.

### Added

- Fit the island to the current Windows taskbar height and display scaling, center it vertically with equal margins, and refine text spacing and corner rounding; allow saved positions over the Windows taskbar, and preserve macOS menu bar and Dock boundaries.
- Add a compact island bar layout with provider labels, quota percentages, progress bars, and stale or unavailable status.
- Place the island at the top center of the current screen on selection or position reset, preserve dragged positions, and resize around its center when subscriptions change.
- Retain reset times and credits in provider tooltips and support existing percentage, opacity, and always-on-top settings.

### Validation

- Pass 101 automated UI checks and verify Windows taskbar alignment and stacking without changing keyboard focus. Native Mac testing of the new island and dragging behavior remains pending.

## [1.5.1] - 2026-09-12

### Fixed

- Lower the Mac bundle minimum to macOS 13.0 following a successful user test on 13.5.
- Keep context and native menu objects alive, defer actions until callbacks return, and refresh menu items before opening rather than during selection.
- Use the explicit Show / hide command on macOS instead of also toggling visibility when the menu bar icon is clicked.
- Generate and register a multi-resolution `.icns` application icon in the Mac bundle before signing.
- Embed the application icon in the Windows executable and explicitly select it for the Start menu shortcut.
- Add UI regression checks that settings selections keep the widget visible and preserve native menu objects.

### Included since v1.4.0

- Replace Windows Forms and Win32/GDI badge rendering with a shared Avalonia/Skia interface.
- Retain provider selection, adaptive layouts, percentage display, saved position, opacity, always-on-top, and usage tooltips.
- Add macOS menu bar controls, Application Support preferences, and CLI discovery. Both installers enable launch at login, with a menu option to disable it.
- Replace Windows-only instance activation with per-user ownership and named-pipe restoration.
- Add self-contained macOS app bundle packaging for Apple Silicon and Intel, install/uninstall scripts, and Windows/macOS CI packaging.
- Preserve existing Windows preferences and startup behavior; copy nested runtime dependencies during installation.
- Add deterministic refresh and rendered UI checks for cancellation, late responses, selection, sizing, transparency, and stale readings.
- Make tray summaries follow the selected used/remaining display mode.

### Known limitations

Mac packages are ad-hoc signed, not Developer ID-signed or notarized. The latest menu fix still needs native Mac verification. macOS 13.5 startup was reported working by the user; macOS 13 remains outside Microsoft's official .NET 10 OS support policy.

## [1.4.0] - 2026-09-11

### Added
- Subscription selection for Codex, Grok, or both, remembered between launches.
- Adaptive card and badge layouts for selected subscriptions.
- A shared provider registry for future integrations; Claude is not yet supported.
- Application version in the widget and tray menu, release history, and Git release tags.

### Changed
- Disabled providers stop refreshing and disappear from summaries, tooltips, usage links, diagnostics, and health snapshots.
- Health snapshots group selected providers under a `Providers` object instead of fixed Codex/Grok fields.
- At least one subscription remains enabled.

### Fixed
- Replace the ambiguous "Show percentage used" toggle with explicit "Percentage remaining" and "Percentage used" choices under "Percentage display".
- Apply percentage display changes immediately to compact badges.

## 1.3.1 - Repository baseline

The initial repository commit (`3e6fefa`) declares version 1.3.1. Earlier release history is not present in this repository.

- Codex and Grok usage monitoring with compact badges and detailed cards.
- Automatic refresh, reset countdowns, balances, and Codex free-reset counts.
- Tray controls, saved preferences, and optional Windows startup.

[Unreleased]: https://github.com/wernerong/ai-usage-widget/compare/v1.7.0...HEAD
[1.7.0]: https://github.com/wernerong/ai-usage-widget/compare/v1.6.0...v1.7.0
[1.6.0]: https://github.com/wernerong/ai-usage-widget/compare/v1.5.1...v1.6.0
[1.5.1]: https://github.com/wernerong/ai-usage-widget/tree/v1.5.1
[1.4.0]: https://github.com/wernerong/ai-usage-widget/tree/v1.4.0
