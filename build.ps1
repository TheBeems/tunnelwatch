param([switch] $Test, [string] $OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$outputDir = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
& (Join-Path $PSScriptRoot 'scripts\Build-Resources.ps1') -OutputDirectory $outputDir
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'No .NET Framework C# compiler was found.' }
$release = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release
if ($release -lt 533320) { throw '.NET Framework 4.8.1 is required.' }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$testSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object FullName)
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.ServiceProcess.dll', '/r:System.Web.Extensions.dll', '/r:System.Management.dll')
$exe = Join-Path $outputDir 'TunnelWatch.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /warnaserror+ "/out:$exe" "/win32manifest:$PSScriptRoot\src\app.manifest" @references @sources @testSources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\App.config') -Destination "$exe.config" -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'scripts\Control-TunnelWatch.ps1') -Destination (Join-Path $outputDir 'Control-TunnelWatch.ps1') -Force
$localConfig = Join-Path $PSScriptRoot 'config.local.json'
if (Test-Path -LiteralPath $localConfig) { Copy-Item -LiteralPath $localConfig -Destination (Join-Path $outputDir 'config.local.json') -Force }
if ($Test) {
    $testReport = Join-Path $outputDir 'self-test.local.json'
    $testProcess = Start-Process -FilePath $exe -ArgumentList @('--self-test', '--report', ('"' + $testReport + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($testProcess.ExitCode -ne 0) { throw 'Tests failed; also check whether Windows application control blocked execution.' }
    Get-Content -LiteralPath $testReport
}
Get-FileHash -LiteralPath $exe -Algorithm SHA256
