param([string]$Version = '0.1.2')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part release version' }
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
if ($props.Project.PropertyGroup.Version -ne $Version) { throw 'Update Directory.Build.props to the release version first.' }
$buildPath = Join-Path $root ('artifacts/build/' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $buildPath 'package'
New-Item -ItemType Directory -Path $package -Force | Out-Null
dotnet run --project tests/Sterling.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw 'Core checks failed' }
dotnet publish src/Sterling.App -c Release -r win-x64 --self-contained true -o $package -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed' }
dotnet publish src/Sterling.Updater -c Release -r win-x64 --self-contained true -o $package -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed' }
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $package
Copy-Item -LiteralPath (Join-Path $root 'docs/RELEASE-NOTES.md') -Destination $package
@{ product = 'Sterling Software Centre'; version = $Version; architecture = 'win-x64' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'sterling-portable.json') -Encoding utf8
$zip = Join-Path $root "artifacts/SterlingSoftwareCentre-v$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
$extract = Join-Path $buildPath 'extracted'
Expand-Archive -LiteralPath $zip -DestinationPath $extract
$smoke = Join-Path $buildPath 'portable-smoke.png'
$app = Start-Process -FilePath (Join-Path $extract 'Sterling.App.exe') -ArgumentList @('--smoke-test', ('"' + $smoke + '"')) -WindowStyle Hidden -PassThru
if (-not $app.WaitForExit(60000)) { $app.Kill(); throw 'Portable WPF smoke test timed out' }
if ($app.ExitCode -ne 0 -or -not (Test-Path -LiteralPath "$smoke.json")) { throw 'Extracted portable WPF smoke test failed' }
if (-not (Test-Path -LiteralPath (Join-Path $extract 'coreclr.dll'))) { throw 'Self-contained runtime missing' }
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon((Join-Path $extract 'Sterling.App.exe'))
if ($null -eq $icon) { throw 'Executable icon missing' }
$icon.ToBitmap().Save((Join-Path $buildPath 'executable-icon.png'))
$icon.Dispose()
foreach ($mode in @('ui-tests', 'startup-test')) {
    $result = Join-Path $buildPath $mode
    $test = Start-Process -FilePath (Join-Path $extract 'Sterling.App.exe') -ArgumentList @("--$mode", ('"' + $result + '"')) -WindowStyle Hidden -PassThru
    if (-not $test.WaitForExit(90000)) { $test.Kill(); throw "$mode timed out" }
    if ($test.ExitCode -ne 0) { throw "$mode failed; inspect $result.error.txt" }
    $proof = if ($mode -eq 'ui-tests') { Join-Path $result 'checks.json' } else { "$result.json" }
    if (-not (Test-Path -LiteralPath $proof)) { throw "$mode produced no verification" }
}
Write-Output "Package: $zip"
Write-Output "SHA-256: $hash"
Write-Output "Smoke render: $smoke"
