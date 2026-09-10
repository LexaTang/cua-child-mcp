param([string]$PackageDirectory = "$PSScriptRoot/../dist")
$ErrorActionPreference = 'Stop'
if (Test-Path (Join-Path $PackageDirectory 'mcp.json')) { throw 'Package contains a build-machine MCP config.' }
$target = Join-Path ([IO.Path]::GetTempPath()) ('cua relocation ' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $target | Out-Null
try {
    Copy-Item (Join-Path $PackageDirectory '*') $target -Recurse
    # Run outside the package directory to catch dependencies on the caller's cwd.
    Push-Location $PSScriptRoot
    try {
        $exe = Join-Path $target 'cua-child.exe'
        $json = & $exe config
        if ($LASTEXITCODE) { throw 'config command failed' }
        $server = ($json | ConvertFrom-Json).mcpServers.'cua-child'
        if ($server.command -ne $exe) { throw 'Config does not point to relocated executable.' }
        if ($server.args[2] -ne (Join-Path $target 'driver/cua-driver.exe')) { throw 'Driver path did not relocate.' }
        if (!(Test-Path -LiteralPath $server.args[2])) { throw 'Bundled driver missing.' }
    } finally { Pop-Location }
    Write-Host 'Portable config verified from another working directory, including paths with spaces.'
} finally {
    # Delete only the exact unique temporary directory created by this invocation.
    $resolved = [IO.Path]::GetFullPath($target)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
