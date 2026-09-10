param([string]$OutputFile = (Join-Path $PWD ('rdp-connection-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')))
# Read-only, run while the viewer is waiting for a connection. No credentials collected.
$ErrorActionPreference = 'Stop'
& {
    "Captured: $(Get-Date -Format o)"
    Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' |
        Select-Object ProductName, DisplayVersion, CurrentBuild, UBR | Format-List | Out-String
    $processes = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -in @('cua-child.exe', 'cua-driver.exe', 'mstsc.exe') })
    'Client / worker processes (no command lines):'
    $processes | Select-Object ProcessId, ParentProcessId, SessionId, ExecutablePath | Format-List | Out-String
    'RDP service:'
    $service = Get-CimInstance Win32_Service -Filter "Name='TermService'"
    $service | Select-Object Name, State, ProcessId | Format-List | Out-String
    'Sessions and listeners:'
    & qwinsta.exe 2>&1 | Out-String
    'Configured ordinary RDP port (may differ from child-session endpoint):'
    Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp' |
        Select-Object PortNumber | Format-List | Out-String
    for ($sample = 1; $sample -le 3; $sample++) {
        "TCP sample $sample : $(Get-Date -Format o)"
        try {
            $tcp = @(Get-NetTCPConnection -ErrorAction Stop)
            $clientTcp = @($tcp | Where-Object { $_.OwningProcess -in $processes.ProcessId })
            'Connections owned by the listed processes:'
            $clientTcp | Select-Object LocalAddress, LocalPort, RemoteAddress, RemotePort, State, OwningProcess |
                Format-Table -AutoSize | Out-String -Width 240
            'Listeners owned by TermService or matching observed destination ports:'
            $tcp | Where-Object { $_.State -eq 'Listen' -and ($_.OwningProcess -eq $service.ProcessId -or $_.LocalPort -in $clientTcp.RemotePort) } |
                Select-Object LocalAddress, LocalPort, OwningProcess | Format-Table -AutoSize | Out-String -Width 240
        } catch { "TCP query failed: $($_.Exception.Message)" }
        if ($sample -lt 3) { Start-Sleep -Seconds 2 }
    }
} | Out-File -LiteralPath $OutputFile -Encoding utf8 -Width 240
Write-Host "Connection snapshot saved: $OutputFile"
