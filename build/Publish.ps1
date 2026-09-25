param([string]$Version = '0.1.0')
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
Write-Output "Package: $zip"
Write-Output "SHA-256: $hash"
Write-Output "Smoke render: $smoke"
