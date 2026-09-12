#!/bin/bash
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'This installer requires macOS.' >&2; exit 1; }
payload_dir="$(cd "$(dirname "$0")" && pwd)"
source_app="$payload_dir/AI Usage Widget.app"
destination="$HOME/Applications/AI Usage Widget.app"
[[ -x "$source_app/Contents/MacOS/AIUsageWidget" ]] || { echo 'Keep Install.command beside AI Usage Widget.app.' >&2; exit 1; }
if pgrep -x AIUsageWidget >/dev/null; then echo 'Exit AI Usage Widget from its menu bar before installing.' >&2; exit 1; fi
mkdir -p "$HOME/Applications"
if [[ -e "$destination" ]]; then
    echo 'An installation already exists. Move it to Trash, then run Install.command again. Preferences are preserved.' >&2
    exit 1
fi
ditto "$source_app" "$destination"
"$destination/Contents/MacOS/AIUsageWidget" --enable-startup
open "$destination"
echo "Installed: $destination"
echo 'Start at login enabled. Disable it from the widget or menu bar menu if desired.'
