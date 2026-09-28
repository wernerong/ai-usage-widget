param([string]$Compiler)
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $repoDir 'source\UsageWidget.csproj'))).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid application version.' }
if (!$Compiler) {
    $Compiler = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'Install Inno Setup 6 or pass -Compiler with the ISCC.exe path.' }
$stage = Join-Path $repoDir ('work\windows-package-' + [guid]::NewGuid().ToString('N'))
$dist = Join-Path $repoDir 'dist'
New-Item -ItemType Directory -Path $stage,$dist -Force | Out-Null
dotnet publish (Join-Path $repoDir 'source\UsageWidget.csproj') -c Release -r win-x64 --self-contained true -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
& $Compiler "/DAppVersion=$version" "/DPayloadDir=$stage" "/DOutputDir=$dist" (Join-Path $repoDir 'installer\windows.iss')
if ($LASTEXITCODE -ne 0) { throw 'Windows installer compilation failed.' }
Write-Output "Installer: $dist\AIUsageWidget-$version-windows-x64-setup.exe"
