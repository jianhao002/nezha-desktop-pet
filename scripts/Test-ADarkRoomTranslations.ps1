param([string]$ReportFile)
$ErrorActionPreference = 'Stop'
$petRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$petLocale = Join-Path $petRoot 'third_party\adarkroom\www\lang\zh_cn'
$petRaw = [IO.File]::ReadAllText((Join-Path $petLocale 'strings.js'))
$petExtra = [IO.File]::ReadAllText((Join-Path $petLocale 'desktop.js'))
$petBase = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$petSupplement = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$petEntries = '"(?<key>(?:\\.|[^"\\])*)"\s*:\s*"(?<value>(?:\\.|[^"\\])*)"'
foreach ($petMatch in [regex]::Matches($petRaw, $petEntries)) {
    $petKey = ('"' + $petMatch.Groups['key'].Value + '"') | ConvertFrom-Json
    [void]$petBase.Add($petKey)
}
foreach ($petMatch in [regex]::Matches($petExtra, $petEntries)) {
    $petKey = ('"' + $petMatch.Groups['key'].Value + '"') | ConvertFrom-Json
    $petValue = ('"' + $petMatch.Groups['value'].Value + '"') | ConvertFrom-Json
    [void]$petSupplement.Add($petKey)
    if ($petValue -notmatch '[\u3400-\u9fff]' -or $petValue -match '[A-Za-z]{3,}') { throw ('补充翻译残留英文：' + $petKey) }
}
$petPattern = '_[(]\s*(?<q>[''"])(?<t>(?:\\.|(?!\k<q>)[^\\])*)\k<q>\s*(?:,|[)])'
$petMissing = @{}
foreach ($petFile in (Get-ChildItem -LiteralPath (Join-Path $petRoot 'third_party\adarkroom\www\script\events') -File -Filter '*.js')) {
    foreach ($petMatch in [regex]::Matches([IO.File]::ReadAllText($petFile.FullName), $petPattern)) {
        $petKey = [regex]::Unescape($petMatch.Groups['t'].Value)
        if (-not $petBase.Contains($petKey) -and -not $petSupplement.Contains($petKey)) { $petMissing[$petKey] = $petFile.Name }
    }
}
if ($petMissing.Count) { throw ('事件文案缺中文：' + (($petMissing.GetEnumerator() | ForEach-Object { $_.Value + ': ' + $_.Key }) -join '; ')) }
$petResult = 'PASS adarkroom-event-literal-translations supplemental=' + $petSupplement.Count + ' missing=0'
if ($ReportFile) { [IO.File]::WriteAllText($ReportFile, $petResult + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) }
Write-Output $petResult
