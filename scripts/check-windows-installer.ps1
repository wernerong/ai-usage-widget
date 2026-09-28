$ErrorActionPreference = 'Stop'
$repoDir = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $repoDir 'source\UsageWidget.csproj'))).Project.PropertyGroup.Version
$installer = Join-Path $repoDir "dist\AIUsageWidget-$version-windows-x64-setup.exe"
$destination = Join-Path $env:TEMP ('AIUsageWidget-installer-check-' + [guid]::NewGuid().ToString('N'))
function Run-Setup {
    $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/LOG', '/MERGETASKS="!startup,!desktopicon"', "/DIR=`"$destination`"") -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        Get-ChildItem $env:TEMP -Filter 'Setup Log*.txt' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content
        throw "Setup failed: $($process.ExitCode)"
    }
}
Run-Setup
$executable = Join-Path $destination 'AIUsageWidget.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Installed executable is missing.' }
if ((Get-Item -LiteralPath $executable).VersionInfo.ProductVersion -notlike "$version*") { throw 'Installed version differs.' }
$check = Start-Process -FilePath $executable -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($check.ExitCode -ne 0) { throw 'Installed self-test failed.' }
Get-Content (Join-Path $destination 'checks.txt')
Run-Setup
$uninstaller = Join-Path $destination 'unins000.exe'
$remove = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
if ($remove.ExitCode -ne 0) { throw 'Uninstall failed.' }
if (Test-Path -LiteralPath $executable) { throw 'Uninstall left the application executable behind.' }
Write-Output 'PASS: native Windows install, installed self-tests, upgrade, and uninstall.'
