param([string]$Cli = "$PSScriptRoot/dist/cua-child.exe", [string]$Driver = "$PSScriptRoot/dist/driver/cua-driver.exe")
$ErrorActionPreference = 'Stop'
function Start-Probe {
    $info = [Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($Cli))
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($arg in @('mcp', '--driver', [IO.Path]::GetFullPath($Driver), '--timeout', '45')) { $info.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    @(
        '{"jsonrpc":"2.0","id":"初始化","method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"child-integration-test","version":"1"}}}',
        '{"jsonrpc":"2.0","method":"notifications/initialized"}',
        '{"jsonrpc":"2.0","id":42,"method":"tools/list","params":{}}',
        '{"jsonrpc":"2.0","id":43,"method":"tools/call","params":{"name":"list_windows","arguments":{}}}',
        '{"jsonrpc":"2.0","id":"unknown","method":"test/unknown"}'
    ) | ForEach-Object { $process.StandardInput.WriteLine($_) }
    $process.StandardInput.Close()
    return @{ Process = $process; Out = $stdout; Err = $stderr }
}
function Complete-Probe($probe) {
    if (!$probe.Process.WaitForExit(60000)) { $probe.Process.Kill($true); throw 'MCP startup/request timed out' }
    if ($probe.Process.ExitCode -ne 0) { throw $probe.Err.Result }
    $messages = @($probe.Out.Result -split "`n" | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
    if ($messages.Count -ne 4) { throw "Unexpected stdout/protocol response count: $($messages.Count)" }
    if ($messages[0].id -ne '初始化' -or !$messages[0].result.serverInfo) { throw 'initialize/UTF-8 mismatch' }
    if ($messages[1].id -ne 42 -or !$messages[1].result.tools.Count) { throw 'Empty tools/list' }
    if ($messages[2].id -ne 43 -or !$messages[2].result -or $messages[2].result.isError) { throw 'Child worker list_windows failed' }
    if ($messages[3].id -ne 'unknown' -or $messages[3].error.code -ne -32601) { throw 'Unknown method response changed' }
    Write-Host "PASS: initialize, UTF-8 IDs, notifications, $($messages[1].result.tools.Count) tools, list_windows, unknown method, EOF."
    $probe.Process.Dispose()
}
Complete-Probe (Start-Probe)
$workers = @(Get-Process cua-driver | Select-Object Id, SessionId | Sort-Object Id)
$probes = @(1..3 | ForEach-Object { Start-Probe })
$probes | ForEach-Object { Complete-Probe $_ }
$after = @(Get-Process cua-driver | Select-Object Id, SessionId | Sort-Object Id)
if (($workers | ConvertTo-Json -Compress) -ne ($after | ConvertTo-Json -Compress)) { throw 'Worker restarted or duplicated during reuse' }
Write-Host 'PASS: three concurrent clients reused the same worker.'
& $Cli status
