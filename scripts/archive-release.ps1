param(
    [string]$Tag = '',
    [string]$InputDirectory = "$PSScriptRoot/../dist",
    [string]$OutputDirectory = "$PSScriptRoot/../release"
)
$ErrorActionPreference = 'Stop'
if ($Tag -and $Tag -notmatch '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
    throw "Invalid release tag '$Tag'; use v1.2.3 or v1.2.3-rc.1."
}
if (!(Test-Path (Join-Path $InputDirectory 'cua-child.exe'))) {
    throw 'Build the portable package before archiving.'
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$name = if ($Tag) { "cua-child-mcp-$Tag-win-x64.zip" } else { 'cua-child-mcp-win-x64.zip' }
$archive = Join-Path $OutputDirectory $name
Compress-Archive -Path (Join-Path $InputDirectory '*') -DestinationPath $archive -Force
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name" | Set-Content (Join-Path $OutputDirectory "$name.sha256") -Encoding ascii
Write-Host "Release archive ready: $archive"
