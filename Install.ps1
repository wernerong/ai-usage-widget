$ErrorActionPreference = 'Stop'
$taskInstallDir = Join-Path $env:LOCALAPPDATA 'Programs\AIUsageWidget'
$taskPayloadDir = Join-Path $PSScriptRoot 'app'
$taskExecutable = Join-Path $taskInstallDir 'AIUsageWidget.exe'
if (!(Test-Path -LiteralPath (Join-Path $taskPayloadDir 'AIUsageWidget.exe'))) {
    throw 'The app folder is missing. Keep Install.ps1 next to the app folder.'
}
$taskRunning = Get-Process AIUsageWidget -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $taskExecutable }
if ($taskRunning) { throw 'Exit AI Usage Widget from its tray menu before reinstalling.' }
New-Item -ItemType Directory -Path $taskInstallDir -Force | Out-Null
Get-ChildItem -LiteralPath $taskPayloadDir | Copy-Item -Destination $taskInstallDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $taskInstallDir -Force
$taskShell = New-Object -ComObject WScript.Shell
$taskShortcut = $taskShell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'AI Usage Widget.lnk'))
$taskShortcut.TargetPath = $taskExecutable
$taskShortcut.WorkingDirectory = $taskInstallDir
$taskShortcut.Description = 'Floating Codex and Grok usage monitor'
$taskShortcut.Save()
$taskRunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (!(Test-Path -LiteralPath $taskRunKey)) { New-Item -Path $taskRunKey | Out-Null }
New-ItemProperty -Path $taskRunKey -Name 'AIUsageWidget' -Value ('"' + $taskExecutable + '"') -PropertyType String -Force | Out-Null
Write-Output "Installed: $taskExecutable"
Write-Output 'Start menu shortcut created. Start with Windows enabled (toggle in the widget menu).'
Start-Process -FilePath $taskExecutable
