$ErrorActionPreference = 'Stop'
$taskExpectedDir = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\AIUsageWidget'))
$taskExecutable = Join-Path $taskExpectedDir 'AIUsageWidget.exe'
$taskRunning = Get-Process AIUsageWidget -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $taskExecutable }
if ($taskRunning) { throw 'Exit AI Usage Widget from its tray menu, then run Uninstall.ps1 again.' }
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'AIUsageWidget' -ErrorAction SilentlyContinue
$taskShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'AI Usage Widget.lnk'
if (Test-Path -LiteralPath $taskShortcut) { Remove-Item -LiteralPath $taskShortcut }
if (Test-Path -LiteralPath $taskExpectedDir) {
    $taskResolved = (Resolve-Path -LiteralPath $taskExpectedDir).ProviderPath
    if ($taskResolved.TrimEnd('\') -ne $taskExpectedDir.TrimEnd('\')) { throw 'Unexpected installation path. Nothing removed.' }
    Remove-Item -LiteralPath $taskResolved -Recurse -Force
}
Write-Output 'AI Usage Widget uninstalled. Your CLI logins and widget preferences are preserved.'
