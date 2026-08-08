param(
    [string]$ProjectRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$sourceDirectories = @('Core', 'Features', 'Infrastructure', 'Modules', 'Shared', 'UI')
$localizationDirectory = Join-Path $projectRoot 'Core\Plugin\State'

function ConvertFrom-CSharpStringLiteral([string]$value) {
    return [Text.RegularExpressions.Regex]::Unescape($value)
}

function Get-SourceFiles {
    $files = foreach ($directory in $sourceDirectories) {
        $path = Join-Path $projectRoot $directory
        if (Test-Path -LiteralPath $path) {
            Get-ChildItem -LiteralPath $path -Recurse -File -Filter '*.cs' |
                Where-Object { $_.FullName -notmatch '\\(bin|obj|Artifacts|SourceSnapshots)\\' }
        }
    }
    return @($files)
}

function Get-StaticLocalizationKeys([IO.FileInfo[]]$files) {
    $callPattern = [Text.RegularExpressions.Regex]::new(
        '(?s)\b(?:T|Tr|Text|TranslateModuleText)\s*\(\s*"((?:\\.|[^"\\])*)"\s*,\s*"((?:\\.|[^"\\])*)"\s*\)',
        [Text.RegularExpressions.RegexOptions]::Compiled)
    $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($file in $files) {
        $content = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8)
        foreach ($match in $callPattern.Matches($content)) {
            $key = ConvertFrom-CSharpStringLiteral $match.Groups[1].Value
            if ($key -match '[\p{IsCJKUnifiedIdeographs}\p{IsCJKCompatibilityIdeographs}]') {
                [void]$keys.Add($key)
            }
        }
    }
    return $keys
}

function Get-LocaleDictionary([string]$languageName) {
    $entryPattern = [Text.RegularExpressions.Regex]::new(
        '(?s)\{\s*"((?:\\.|[^"\\])*)"\s*,\s*"((?:\\.|[^"\\])*)"\s*\}',
        [Text.RegularExpressions.RegexOptions]::Compiled)
    $result = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $sources = @(Get-ChildItem -LiteralPath $localizationDirectory -File -Filter "*.Localization.$languageName*.cs")
    if ($sources.Count -eq 0) {
        throw "No $languageName localization dictionary source was found in $localizationDirectory."
    }

    foreach ($file in $sources) {
        $content = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8)
        foreach ($match in $entryPattern.Matches($content)) {
            $key = ConvertFrom-CSharpStringLiteral $match.Groups[1].Value
            $value = ConvertFrom-CSharpStringLiteral $match.Groups[2].Value
            if ($result.ContainsKey($key)) {
                if (-not [StringComparer]::Ordinal.Equals($result[$key], $value)) {
                    throw "$languageName localization key '$key' has conflicting values."
                }
                continue
            }
            $result.Add($key, $value)
        }
    }
    return $result
}

function Assert-LocaleCoverage(
    [string]$languageName,
    [Collections.Generic.HashSet[string]]$keys,
    [Collections.Generic.Dictionary[string, string]]$dictionary) {
    $missing = [Collections.Generic.List[string]]::new()
    $empty = [Collections.Generic.List[string]]::new()
    foreach ($key in $keys) {
        if (-not $dictionary.ContainsKey($key)) {
            $missing.Add($key)
        }
        elseif ([string]::IsNullOrWhiteSpace($dictionary[$key])) {
            $empty.Add($key)
        }
    }

    if ($missing.Count -ne 0 -or $empty.Count -ne 0) {
        $details = [Collections.Generic.List[string]]::new()
        if ($missing.Count -ne 0) {
            $details.Add("Missing keys:`n" + (($missing | Sort-Object | ForEach-Object { "  $_" }) -join "`n"))
        }
        if ($empty.Count -ne 0) {
            $details.Add("Empty translations:`n" + (($empty | Sort-Object | ForEach-Object { "  $_" }) -join "`n"))
        }
        throw "$languageName localization coverage failed ($($dictionary.Count) dictionary entries, $($keys.Count) required keys).`n$($details -join "`n")"
    }
}

$sourceFiles = Get-SourceFiles
$staticKeys = Get-StaticLocalizationKeys $sourceFiles
$japanese = Get-LocaleDictionary 'Japanese'
$russian = Get-LocaleDictionary 'Russian'

Assert-LocaleCoverage 'Japanese' $staticKeys $japanese
Assert-LocaleCoverage 'Russian' $staticKeys $russian

Write-Host "Localization verification passed: $($staticKeys.Count) static Chinese UI keys; Japanese $($staticKeys.Count)/$($staticKeys.Count), Russian $($staticKeys.Count)/$($staticKeys.Count)."
