param([string]$OutputDirectory = "$PSScriptRoot/dist", [string]$Version = "0.26.1")
$ErrorActionPreference = 'Stop'
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$cache = Join-Path $PSScriptRoot '.cache'
New-Item -ItemType Directory -Force $cache | Out-Null
$asset = "cua-driver-rs-$Version-windows-x86_64.zip"
$base = "https://github.com/trycua/cua/releases/download/cua-driver-rs-v$Version"
$archive = Join-Path $cache $asset
if (!(Test-Path $archive)) { Invoke-WebRequest "$base/$asset" -OutFile $archive }
$checksums = (Invoke-WebRequest "$base/checksums.txt").Content
if ($checksums -is [byte[]]) { $checksums = [Text.Encoding]::UTF8.GetString($checksums) }
$line = ($checksums -split "`n" | Where-Object { $_.Trim().EndsWith($asset) })
if (@($line).Count -ne 1) { throw "Missing or ambiguous checksum for $asset" }
$expected = ($line.Trim() -split '\s+')[0]
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Driver checksum mismatch' }
$driverDir = Join-Path $OutputDirectory 'driver'
Expand-Archive $archive -DestinationPath $driverDir -Force
$binary = Get-ChildItem $driverDir -Filter cua-driver.exe -Recurse | Select-Object -First 1
if (!$binary) { throw 'Release contains no cua-driver.exe' }
if ($binary.DirectoryName -ne $driverDir) { Copy-Item "$($binary.DirectoryName)\*" $driverDir -Recurse -Force }
dotnet publish "$PSScriptRoot/CuaChild.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $OutputDirectory --nologo
if ($LASTEXITCODE) { throw 'dotnet publish failed' }
Copy-Item "$PSScriptRoot/README.md" $OutputDirectory -Force
Copy-Item "$PSScriptRoot/scripts/collect-rdp-connection.ps1" $OutputDirectory -Force
Copy-Item "$PSScriptRoot/assets" $OutputDirectory -Recurse -Force
Copy-Item "$PSScriptRoot/LICENSE" $OutputDirectory -Force
Copy-Item "$PSScriptRoot/CUA-LICENSE.md" (Join-Path $OutputDirectory 'CUA-LICENSE') -Force
# A portable archive must not ship a config pointing to the build machine.
# Remove the file produced by older versions when reusing an output directory.
$oldConfig = Join-Path $OutputDirectory 'mcp.json'
if (Test-Path -LiteralPath $oldConfig) { Remove-Item -LiteralPath $oldConfig }
"Driver=$Version`nAsset=$asset`nSHA256=$expected" | Set-Content (Join-Path $OutputDirectory 'driver-version.txt')
Write-Host "Portable package ready: $OutputDirectory"
