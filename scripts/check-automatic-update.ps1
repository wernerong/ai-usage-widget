param([Parameter(Mandatory)][string]$Runtime)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $repo 'source/UsageWidget.csproj'))).Project.PropertyGroup.Version
$stage = Join-Path $repo ('work/update-check-' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $stage 'publish'
$releases = Join-Path $stage 'old-release'
$feed = Join-Path $stage 'feed'
$installed = Join-Path $stage 'installed'
New-Item -ItemType Directory $stage,$feed,$installed -Force | Out-Null
$vpk = Join-Path $repo ('work/vpk/' + $(if ($IsWindows) { 'vpk.exe' } else { 'vpk' }))
$main = if ($IsWindows) { 'AIUsageWidget.exe' } else { 'AIUsageWidget' }
function Run-Widget([string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($executable)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    try { $process.WaitForExit(); return $process.ExitCode } finally { $process.Dispose() }
}
dotnet publish (Join-Path $repo 'source/UsageWidget.csproj') -c Release -r $Runtime --self-contained true -p:Version=0.0.1 -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Old-version test publish failed.' }
$packArgs = @('pack','--packId','AIUsageWidget.Desktop','--packVersion','0.0.1','--packDir',$publish,'--mainExe',$main,'--packTitle','AI Usage Widget','--channel',$Runtime,'--runtime',$Runtime,'--outputDir',$releases,'--noInst')
if (!$IsWindows) { $packArgs += @('--signAppIdentity','-') }
& $vpk @packArgs
if ($LASTEXITCODE -ne 0) { throw 'Old-version update package failed.' }
$portable = (Get-ChildItem $releases -Filter '*Portable.zip').FullName
if ($IsWindows) {
    Expand-Archive -LiteralPath $portable -DestinationPath $installed
    $executable = Join-Path $installed 'current/AIUsageWidget.exe'
} else {
    & /usr/bin/ditto -x -k $portable $installed
    if ($LASTEXITCODE -ne 0) { throw 'Test bundle extraction failed.' }
    $executable = Join-Path $installed 'AI Usage Widget.app/Contents/MacOS/AIUsageWidget'
}
Copy-Item (Join-Path $repo "dist/releases.$Runtime.json") $feed
Copy-Item (Join-Path $repo "dist/*-$Runtime-full.nupkg") $feed
# Deliberately corrupt the package first. The updater must reject it without
# replacing the installed executable or writing a successful restart receipt.
$package = (Get-ChildItem $feed -Filter '*.nupkg').FullName
$originalHash = (Get-FileHash $executable).Hash
$stream = [IO.File]::OpenWrite($package)
try { $stream.SetLength(16) } finally { $stream.Dispose() }
if ((Run-Widget @('--update-smoke',$feed)) -eq 0) { throw 'Corrupt update was accepted.' }
if ((Get-FileHash $executable).Hash -ne $originalHash) { throw 'Corrupt update changed installed files.' }
Copy-Item (Join-Path $repo "dist/*-$Runtime-full.nupkg") $feed -Force
if ((Run-Widget @('--update-smoke',$feed)) -ne 0) { throw 'Valid update could not start.' }
$receipt = Join-Path $feed 'updated-version.txt'
$deadline = [DateTime]::UtcNow.AddSeconds(90)
while (!(Test-Path $receipt) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
if (!(Test-Path $receipt) -or (Get-Content $receipt -Raw).Trim() -ne $version) { throw 'Updater did not restart into the new version.' }
if ((Run-Widget @('--verify-update-install')) -ne 0) { throw 'New installation lost updater support.' }
Write-Output "PASS: $Runtime rejects corrupt updates, upgrades 0.0.1 to $version, and restarts automatically."
