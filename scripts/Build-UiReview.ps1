$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectDir = Split-Path $PSScriptRoot
$outDir = Join-Path $projectDir 'artifacts\ux-review\fixture'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
& (Join-Path $PSScriptRoot 'Build-Resources.ps1') -OutputDirectory $outDir
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe' }
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectDir 'src') -Filter '*.cs' | ForEach-Object FullName)
$testFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectDir 'tests') -Filter '*.cs' | ForEach-Object FullName)
& $compilerPath /nologo /target:winexe /main:TunnelWatch.UiReview /define:UI_REVIEW /platform:anycpu /optimize+ /warn:4 /warnaserror+ "/out:$outDir\TunnelWatch-UiReview.exe" "/win32manifest:$projectDir\src\app.manifest" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.ServiceProcess.dll /r:System.Web.Extensions.dll /r:System.Management.dll @sourceFiles @testFiles
if ($LASTEXITCODE -ne 0) { throw 'UI review build failed.' }
Copy-Item -LiteralPath (Join-Path $projectDir 'src\App.config') -Destination (Join-Path $outDir 'TunnelWatch-UiReview.exe.config') -Force
Get-FileHash -LiteralPath (Join-Path $outDir 'TunnelWatch-UiReview.exe')
