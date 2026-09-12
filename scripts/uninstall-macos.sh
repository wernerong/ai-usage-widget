#!/bin/bash
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'This uninstaller requires macOS.' >&2; exit 1; }
if pgrep -x AIUsageWidget >/dev/null; then echo 'Exit AI Usage Widget from its menu bar before uninstalling.' >&2; exit 1; fi
launch_agent="$HOME/Library/LaunchAgents/com.wernerong.ai-usage-widget.plist"
if [[ -f "$launch_agent" ]]; then
    launchctl bootout "gui/$(id -u)" "$launch_agent" 2>/dev/null || true
    rm -f "$launch_agent"
fi
app="$HOME/Applications/AI Usage Widget.app"
if [[ -e "$app" ]]; then
    mkdir -p "$HOME/.Trash"
    mv "$app" "$HOME/.Trash/AI Usage Widget-$(date +%Y%m%d-%H%M%S)-$$.app"
fi
echo 'AI Usage Widget removed. Preferences and provider logins are preserved.'
