$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
$output = Join-Path $projectRoot 'artifacts\control-preflight'
New-Item -ItemType Directory -Path $output -Force | Out-Null
& (Join-Path $projectRoot 'scripts\Build-Resources.ps1') -OutputDirectory $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\Control-TunnelWatch.ps1') -Destination (Join-Path $output 'Control-TunnelWatch.ps1') -Force
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $operatorSid = $identity.User.Value } finally { $identity.Dispose() }
foreach ($action in @('WssStart','WssStop','Split','Full','Off')) {
    $operation = [guid]::NewGuid().ToString('N')
    $report = Join-Path $output ($action + '.local.json')
    & (Join-Path $output 'Control-TunnelWatch.ps1') -Action $action -UserSid $operatorSid -OperationId $operation -ResultPath $report -ValidateOnly
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (-not $result.Success -or $result.OperationId -ne $operation) { throw ('Preflight ' + $action + ' mislukt: ' + $result.Message) }
}
'5 preflightcontroles geslaagd; geen netwerkacties uitgevoerd.'
