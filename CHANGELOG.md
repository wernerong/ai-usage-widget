# Changelog

User-visible changes are recorded here, newest first. Versions follow semantic versioning.

## [Unreleased]

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
