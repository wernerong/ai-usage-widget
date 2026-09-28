$ErrorActionPreference = 'Stop'
$repoDir = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $repoDir 'source\UsageWidget.csproj'))).Project.PropertyGroup.Version
$installer = Join-Path $repoDir "dist\AIUsageWidget-$version-windows-x64-setup.exe"
$destination = Join-Path $env:TEMP ('AIUsageWidget-installer-check-' + [guid]::NewGuid().ToString('N'))
function Run-Setup([string]$Setup = $installer) {
    $process = Start-Process -FilePath $Setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/LOG', '/MERGETASKS="!startup,!desktopicon"', "/DIR=`"$destination`"") -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        Get-ChildItem $env:TEMP -Filter 'Setup Log*.txt' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content
        throw "Setup failed: $($process.ExitCode)"
    }
}
# Exercise the one-time migration from the previously published flat layout.
$legacy = Join-Path $env:TEMP ('AIUsageWidget-legacy-' + [guid]::NewGuid().ToString('N') + '.exe')
Invoke-WebRequest 'https://github.com/wernerong/ai-usage-widget/releases/download/v1.10.0/AIUsageWidget-1.10.0-windows-x64-setup.exe' -OutFile $legacy
if ((Get-FileHash $legacy -Algorithm SHA256).Hash -ine '5d9e65d207798bc15b126fca74a7fd4524bb548507441e413a2124331d89d893') { throw 'Legacy installer checksum mismatch.' }
Run-Setup $legacy
if (!(Test-Path (Join-Path $destination 'AIUsageWidget.exe'))) { throw 'Legacy installation failed.' }
Run-Setup
$executable = Join-Path $destination 'current\AIUsageWidget.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Installed executable is missing.' }
if ((Get-Item -LiteralPath $executable).VersionInfo.ProductVersion -notlike "$version*") { throw 'Installed version differs.' }
$check = Start-Process -FilePath $executable -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($check.ExitCode -ne 0) { throw 'Installed self-test failed.' }
Get-Content (Join-Path $destination 'current\checks.txt')
$updaterCheck = Start-Process -FilePath $executable -ArgumentList '--verify-update-install' -WindowStyle Hidden -Wait -PassThru
if ($updaterCheck.ExitCode -ne 0) { throw 'Installed updater is missing.' }
Run-Setup
$uninstaller = Join-Path $destination 'unins000.exe'
$remove = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
if ($remove.ExitCode -ne 0) { throw 'Uninstall failed.' }
if (Test-Path -LiteralPath $executable) { throw 'Uninstall left the application executable behind.' }
Write-Output 'PASS: native Windows install, installed self-tests, upgrade, and uninstall.'
