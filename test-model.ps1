# Portable state-model tests on the installed .NET SDK, without NuGet/restore.
# Includes read-only Windows error-message lookup; no Forms, route or VPN APIs.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$sdk = & dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'No usable .NET SDK was found.' }
$sdkVersion = $Matches[1]
$sdkRoot = $Matches[2]
$dotnetRoot = Split-Path $sdkRoot
$refRoot = Join-Path $dotnetRoot 'packs\Microsoft.NETCore.App.Ref'
$pack = Get-ChildItem -LiteralPath $refRoot -Directory | Sort-Object { [version]$_.Name } | Select-Object -Last 1
$ref = Get-ChildItem -LiteralPath (Join-Path $pack.FullName 'ref') -Directory | Select-Object -Last 1
$out = Join-Path $PSScriptRoot 'artifacts\model-tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
& (Join-Path $PSScriptRoot 'scripts\Build-Resources.ps1') -OutputDirectory $out
$assembly = Join-Path $out 'ModelTests.dll'
$compileArgs = @('/nologo', '/target:exe', '/define:CORE_TESTS', '/warn:4', '/warnaserror+', "/out:$assembly")
$compileArgs += @(Get-ChildItem -LiteralPath $ref.FullName -Filter '*.dll' | ForEach-Object { '/r:' + $_.FullName })
$compileArgs += @((Join-Path $PSScriptRoot 'src\Model.cs'), (Join-Path $PSScriptRoot 'src\L.cs'), (Join-Path $PSScriptRoot 'src\SystemErrors.cs'), (Join-Path $PSScriptRoot 'src\LanguagePreferences.cs'), (Join-Path $PSScriptRoot 'tests\Tests.cs'), (Join-Path $PSScriptRoot 'tests\CoreMain.cs'))
$compileArgs += (Join-Path $PSScriptRoot 'tests\SettingsTests.cs')
foreach ($name in @('SettingsRules.cs','RelayAddress.cs','ConnectionView.cs','TcpPeerCodec.cs')) {
    $candidate = Join-Path $PSScriptRoot ('src\' + $name)
    if (Test-Path -LiteralPath $candidate) { $compileArgs += $candidate }
}
& dotnet (Join-Path $sdkRoot "$sdkVersion\Roslyn\bincore\csc.dll") @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Model test build failed.' }
$runtime = & dotnet --list-runtimes | Where-Object { $_ -like 'Microsoft.NETCore.App *' } | Select-Object -Last 1
$runtimeVersion = ($runtime -split ' ')[1]
@{ runtimeOptions = @{ tfm = $ref.Name; framework = @{ name = 'Microsoft.NETCore.App'; version = $runtimeVersion } } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'ModelTests.runtimeconfig.json') -Encoding utf8
& dotnet $assembly
if ($LASTEXITCODE -ne 0) { throw 'Model tests failed.' }
