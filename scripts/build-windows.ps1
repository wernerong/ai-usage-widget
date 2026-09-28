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
$tools = Join-Path $repoDir 'work\vpk'
if (!(Test-Path (Join-Path $tools 'vpk.exe'))) {
    dotnet tool install vpk --tool-path $tools --version 1.2.158
    if ($LASTEXITCODE -ne 0) { throw 'Updater tool installation failed.' }
}
$updates = $stage + '-updates'
& (Join-Path $tools 'vpk.exe') pack --packId AIUsageWidget.Desktop --packVersion $version --packDir $stage --mainExe AIUsageWidget.exe --packTitle 'AI Usage Widget' --channel win-x64 --runtime win-x64 --outputDir $updates --icon (Join-Path $repoDir 'source\Assets\widget.ico') --noInst
if ($LASTEXITCODE -ne 0) { throw 'Updater packaging failed.' }
$payload = Join-Path $stage 'installer-payload'
Expand-Archive -LiteralPath (Get-ChildItem $updates -Filter '*Portable.zip').FullName -DestinationPath $payload
# Keep the existing shortcuts, startup entries and Antigravity hook working.
Move-Item -LiteralPath (Join-Path $payload 'AI Usage Widget.exe') -Destination (Join-Path $payload 'AIUsageWidget.exe')
Copy-Item (Join-Path $updates '*.nupkg'),(Join-Path $updates 'releases.*.json') $dist
& $Compiler "/DAppVersion=$version" "/DPayloadDir=$payload" "/DOutputDir=$dist" (Join-Path $repoDir 'installer\windows.iss')
if ($LASTEXITCODE -ne 0) { throw 'Windows installer compilation failed.' }
Write-Output "Installer: $dist\AIUsageWidget-$version-windows-x64-setup.exe"
