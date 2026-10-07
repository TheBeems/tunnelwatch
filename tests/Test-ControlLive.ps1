param([Parameter(Mandatory=$true)][string]$UserSid)
# Explicit opt-in live test: changes the current local VPN and restores split.
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot
$output = Join-Path $project 'artifacts\control-live'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$summaryPath = Join-Path $output 'summary.local.json'
$control = Join-Path $project 'bin\Control-TunnelWatch.ps1'
$app = Join-Path $project 'bin\TunnelWatch.exe'
$powershellExe = Join-Path $PSHOME 'powershell.exe'
$steps = @()
$summary = [ordered]@{ Success=$false; Restored=$false; StartedAt=(Get-Date).ToString('o'); Steps=@(); Error=$null }
function Save-Summary { $summary.Steps = $script:steps; $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8 }
function Invoke-Control([string]$Action) {
    $id = [guid]::NewGuid().ToString('N')
    $report = Join-Path $output ($Action + '-action.local.json')
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $control + '" -Action ' + $Action + ' -UserSid ' + $UserSid + ' -OperationId ' + $id + ' -ResultPath "' + $report + '"'
    $child = Start-Process -FilePath $powershellExe -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if (-not (Test-Path -LiteralPath $report)) { throw ($Action + ': geen actieresultaat, exit ' + $child.ExitCode) }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($child.ExitCode -ne 0 -or -not $result.Success -or $result.OperationId -ne $id) { throw ($Action + ': ' + $result.Message) }
}
function Observe([string]$Name) {
    $report = Join-Path $output ($Name + '-status.local.json')
    $child = Start-Process -FilePath $app -ArgumentList @('--observe','--report',('"' + $report + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($child.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) { throw ($Name + ': native app-observatie mislukt') }
    return (Get-Content -LiteralPath $report -Raw | ConvertFrom-Json)
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if ($identity.User.Value -ne $UserSid -or -not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Zelfde gebruiker en UAC-goedgekeurde uitvoering vereist.' }
} finally { $identity.Dispose() }
$baseline = Observe 'Before'
if ($baseline.Status.Profile -ne 'split' -or $baseline.Status.Health -ne 2) { throw 'Deze live test vereist een gezonde split-verbinding als uitgangspunt; niets gewijzigd.' }
Save-Summary
try {
    foreach ($step in @(
        @{Action='WssStop';Profile='split';Health=3},
        @{Action='WssStart';Profile='split';Health=2},
        @{Action='Off';Profile='off';Health=-1},
        @{Action='Split';Profile='split';Health=2},
        @{Action='Full';Profile='full';Health=2}
    )) {
        Invoke-Control $step.Action
        $current = Observe $step.Action
        # Allow handshake/route changes to settle; never accept an old runtime Success flag.
        for ($attempt=0; $attempt -lt 8; $attempt++) {
            $matches = $current.Status.Profile -eq $step.Profile -and ($current.Status.Health -eq $step.Health -or ($step.Health -eq -1 -and $current.Status.Health -in @(0,4)))
            if ($matches) { break }
            Start-Sleep -Seconds 1
            $current = Observe $step.Action
        }
        $script:steps += [ordered]@{ Action=$step.Action; Passed=[bool]$matches; Profile=$current.Status.Profile; Health=$current.Status.Health; Reason=$current.Status.Reason }
        Save-Summary
        if (-not $matches) { throw ($step.Action + ': onverwachte actuele status: ' + $current.Status.Reason) }
    }
    $summary.Success = $true
} catch { $summary.Error = $_.Exception.Message }
finally {
    try {
        Invoke-Control 'Split'
        $restored = Observe 'Restored'
        $summary.Restored = $restored.Status.Profile -eq 'split' -and $restored.Status.Health -eq 2
        if (-not $summary.Restored) { $summary.Error = 'Split aangevraagd, maar niet gezond bevestigd: ' + $restored.Status.Reason; $summary.Success = $false }
    } catch { $summary.Error = 'Herstellen naar split mislukt: ' + $_.Exception.Message; $summary.Success = $false }
    $summary.FinishedAt = (Get-Date).ToString('o')
    Save-Summary
}
if (-not $summary.Success) { exit 1 }
