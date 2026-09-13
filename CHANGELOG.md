# Changelog

User-visible changes are recorded here, newest first. Versions follow semantic versioning.

## [Unreleased]

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

[Unreleased]: https://github.com/wernerong/ai-usage-widget/compare/v1.6.0...HEAD
[1.6.0]: https://github.com/wernerong/ai-usage-widget/compare/v1.5.1...v1.6.0
[1.5.1]: https://github.com/wernerong/ai-usage-widget/tree/v1.5.1
[1.4.0]: https://github.com/wernerong/ai-usage-widget/tree/v1.4.0
