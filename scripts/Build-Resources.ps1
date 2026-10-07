# String-only RESX compilation, using installed .NET; no SDK tools or NuGet.
param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = Join-Path (Split-Path $PSScriptRoot) 'resources'
$destination = Join-Path $OutputDirectory 'locales'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
function Read-Strings([string]$Path) {
    $options = [Xml.XmlReaderSettings]::new()
    $options.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $options.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($Path, $options)
    $xml = [Xml.XmlDocument]::new()
    $xml.XmlResolver = $null
    try { $xml.Load($reader) } finally { $reader.Dispose() }
    $strings = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $xml.SelectNodes('/root/data')) {
        $name = $entry.GetAttribute('name')
        if (-not $name -or $entry.HasAttribute('type') -or $entry.HasAttribute('mimetype') -or -not $entry.SelectSingleNode('value')) { throw "Only named string resources are supported: $Path" }
        if ($strings.ContainsKey($name)) { throw "Duplicate resource $name in $Path" }
        $strings.Add($name, $entry.SelectSingleNode('value').InnerText)
    }
    return ,$strings
}
function Get-Placeholders([string]$Text) {
    # Validate composite formatting as well as the numbered argument set.
    $arguments = [object[]]@(0..31 | ForEach-Object { 'test' })
    [string]::Format([Globalization.CultureInfo]::InvariantCulture, $Text, $arguments) | Out-Null
    return (@([regex]::Matches($Text, '(?<!\{)\{(\d+)(?:[,}:])') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique) -join ',')
}
$english = Read-Strings (Join-Path $source 'Strings.resx')
$project = Split-Path $PSScriptRoot
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $project 'src') -Filter '*.cs') {
    foreach ($reference in [regex]::Matches([IO.File]::ReadAllText($file.FullName), 'L\.(?:Get|Format)\("([^"]+)"')) {
        $key = $reference.Groups[1].Value
        if (-not $english.ContainsKey($key)) { throw "Missing English resource $key referenced in $($file.Name)" }
    }
}
foreach ($reference in [regex]::Matches([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Control-TunnelWatch.ps1')), "Get-UiText '([^']+)'")) {
    $key = $reference.Groups[1].Value
    if (-not $english.ContainsKey($key)) { throw "Missing English helper resource $key" }
}
foreach ($file in Get-ChildItem -LiteralPath $source -Filter 'Strings*.resx') {
    if ($file.BaseName -ne 'Strings') {
        if ($file.BaseName -notmatch '^Strings\.(.+)$') { throw "Invalid resource filename: $($file.Name)" }
        $culture = [Globalization.CultureInfo]::GetCultureInfo($Matches[1])
        if ($culture.Name -ne $Matches[1] -or $culture.Name -eq 'en') { throw "Use canonical culture names and Strings.resx for English: $($file.Name)" }
    }
    $strings = Read-Strings $file.FullName
    foreach ($name in $strings.Keys) {
        if (-not $english.ContainsKey($name)) { throw "Unknown translation key $name in $($file.Name)" }
        if ((Get-Placeholders $strings[$name]) -ne (Get-Placeholders $english[$name])) { throw "Translation placeholders differ for $name in $($file.Name)" }
    }
    $writer = [Resources.ResourceWriter]::new((Join-Path $destination ($file.BaseName + '.resources')))
    try { foreach ($name in $strings.Keys) { $writer.AddResource($name, $strings[$name]) }; $writer.Generate() } finally { $writer.Dispose() }
}
