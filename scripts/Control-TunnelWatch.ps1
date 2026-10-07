param(
    [Parameter(Mandatory=$true)][ValidateSet('WssStart','WssStop','Split','Full','Off')][string]$Action,
    [Parameter(Mandatory=$true)][string]$UserSid,
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-f0-9]{32}$')][string]$OperationId,
    [Parameter(Mandatory=$true)][string]$ResultPath,
    [string]$Language = 'en',
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskName = 'TunnelWatch-WSS-WireGuard-transport'
$runtime = Join-Path $env:LOCALAPPDATA 'TunnelWatchWss'
$expectedWss = Join-Path $runtime 'wstunnel.exe'
$fullHelper = Join-Path $runtime 'TunnelWatch-Wss-Full.ps1'
$splitService = 'WireGuardTunnel$tunnelwatch-wss-split'
$fullService = 'WireGuardTunnel$tunnelwatch-wss-full'
$uiCulture = [Globalization.CultureInfo]::GetCultureInfo($Language)
$uiResources = [Resources.ResourceManager]::CreateFileBasedResourceManager('Strings', (Join-Path $PSScriptRoot 'locales'), $null)
function Get-UiText([string]$Key, [object[]]$Arguments = @()) {
    $text = $uiResources.GetString($Key, $uiCulture)
    if ($null -eq $text) { throw "Missing English resource: $Key" }
    return [string]::Format([Globalization.CultureInfo]::CurrentCulture, $text, $Arguments)
}

function Get-VerifiedOwners {
    $owners = @()
    foreach ($endpoint in @(Get-NetUDPEndpoint -LocalPort 39075 -ErrorAction SilentlyContinue | Where-Object { $_.LocalAddress -eq '127.0.0.1' })) {
        $owner = Get-Process -Id $endpoint.OwningProcess -ErrorAction Stop
        if (-not [string]::Equals($owner.Path, $expectedWss, [StringComparison]::OrdinalIgnoreCase)) { throw (Get-UiText 'Helper.UnexpectedOwner') }
        $owners += [pscustomobject]@{ Id=$owner.Id; StartTime=$owner.StartTime; Path=$owner.Path }
    }
    return $owners
}
function Invoke-FullHelper([string]$Mode) {
    # Isolate the original helper's preferences/scope; child inherits elevation and waits.
    $helperArguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $fullHelper + '" -Action ' + $Mode
    $helper = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $helperArguments -WindowStyle Hidden -PassThru -Wait
    if ($helper.ExitCode -ne 0) { throw (Get-UiText 'Helper.FullError' @($helper.ExitCode)) }
}

$success = $false
$message = (Get-UiText 'Helper.NotPerformed')
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        if ($identity.User.Value -ne $UserSid) { throw (Get-UiText 'Helper.UserMismatch') }
        $administrator = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        if (-not $ValidateOnly -and -not $administrator) { throw (Get-UiText 'Helper.UacRequired') }
    } finally { $identity.Dispose() }
    if (-not (Test-Path -LiteralPath $expectedWss)) { throw (Get-UiText 'Helper.InstallationMissing') }
    if ($Action -in @('Split','Full','Off') -and -not (Test-Path -LiteralPath $fullHelper)) { throw (Get-UiText 'Helper.FullMissing') }
    if ($ValidateOnly) {
        $message = (Get-UiText 'Helper.Validated')
    } else {
        switch ($Action) {
            'WssStart' {
                Start-ScheduledTask -TaskName $taskName
                $ready = $false
                for ($attempt=0; $attempt -lt 30; $attempt++) {
                    if (@(Get-VerifiedOwners).Count -eq 1) { $ready = $true; break }
                    Start-Sleep -Milliseconds 500
                }
                if (-not $ready) { throw (Get-UiText 'Helper.ListenerMissing') }
                $message = (Get-UiText 'Helper.WssStarted')
            }
            'WssStop' {
                $owners = @(Get-VerifiedOwners)
                Stop-ScheduledTask -TaskName $taskName
                for ($attempt=0; $attempt -lt 20; $attempt++) {
                    if ((Get-ScheduledTask -TaskName $taskName).State -ne 'Running') { break }
                    Start-Sleep -Milliseconds 250
                }
                if ((Get-ScheduledTask -TaskName $taskName).State -eq 'Running') { throw (Get-UiText 'Helper.TaskStopping') }
                foreach ($owner in $owners) {
                    $current = Get-Process -Id $owner.Id -ErrorAction SilentlyContinue
                    if ($current -and $current.StartTime -eq $owner.StartTime -and [string]::Equals($current.Path, $expectedWss, [StringComparison]::OrdinalIgnoreCase)) {
                        # Exact verified process object; no broad process-name termination.
                        $current.Kill()
                        if (-not $current.WaitForExit(3000)) { throw (Get-UiText 'Helper.ChildStopping') }
                    }
                }
                if (@(Get-VerifiedOwners).Count -ne 0) { throw (Get-UiText 'Helper.ListenerRemains') }
                $message = (Get-UiText 'Helper.WssStopped')
            }
            'Split' {
                Invoke-FullHelper 'Stop'
                if ((Get-Service -Name $splitService).Status -ne 'Running') { throw (Get-UiText 'Helper.SplitInactive') }
                $message = (Get-UiText 'Helper.SplitRequested')
            }
            'Full' {
                Invoke-FullHelper 'Start'
                if ((Get-Service -Name $fullService).Status -ne 'Running') { throw (Get-UiText 'Helper.FullInactive') }
                $message = (Get-UiText 'Helper.FullRequested')
            }
            'Off' {
                # Cleanup full state first, even if a service was manually stopped earlier.
                $fullBefore = Get-Service -Name $fullService -ErrorAction SilentlyContinue
                $fullState = Join-Path $env:ProgramData 'TunnelWatchWssFull\active.json'
                if (($fullBefore -and $fullBefore.Status -ne 'Stopped') -or (Test-Path -LiteralPath $fullState)) { Invoke-FullHelper 'Stop' }
                $split = Get-Service -Name $splitService -ErrorAction SilentlyContinue
                if ($split -and $split.Status -ne 'Stopped') { Stop-Service -Name $splitService; $split.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(10)) }
                $full = Get-Service -Name $fullService -ErrorAction SilentlyContinue
                if ($full -and $full.Status -ne 'Stopped') { throw (Get-UiText 'Helper.FullStillActive') }
                $message = (Get-UiText 'Helper.Off')
            }
        }
    }
    $success = $true
} catch { $message = $_.Exception.Message }
@{ OperationId=$OperationId; Success=$success; Message=$message } | ConvertTo-Json -Compress | Set-Content -LiteralPath $ResultPath -Encoding UTF8
if (-not $success) { exit 1 }
