# Changelog

User-visible changes are recorded here, newest first. Versions follow semantic versioning.

## [Unreleased]

### 1.5.0 — Windows and macOS migration

- Replace Windows Forms and Win32/GDI badge rendering with a shared Avalonia/Skia interface.
- Retain provider selection, adaptive layouts, percentage display, saved position, opacity, always-on-top, and usage tooltips.
- Add macOS menu bar controls, Application Support preferences, and CLI discovery. Both installers enable launch at login, with a menu option to disable it.
- Replace Windows-only instance activation with per-user ownership and named-pipe restoration.
- Add self-contained macOS app bundle packaging for Apple Silicon and Intel, install/uninstall scripts, and Windows/macOS CI packaging.
- Preserve existing Windows preferences and startup behavior; copy nested runtime dependencies during installation.
- Add deterministic refresh and rendered UI checks for cancellation, late responses, selection, sizing, transparency, and stale readings.
- Make tray summaries follow the selected used/remaining display mode.

macOS native smoke testing and Developer ID signing/notarization remain release prerequisites. No v1.5.0 release tag has been created.

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

[Unreleased]: https://github.com/wernerong/ai-usage-widget/compare/v1.4.0...HEAD
[1.4.0]: https://github.com/wernerong/ai-usage-widget/tree/v1.4.0
