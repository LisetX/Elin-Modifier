param(
    [int]$ExpectedHarmonyPatchTypes = 179,
    [int]$MaximumSourceFileLines = 1000
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

Push-Location $projectRoot
try {
    $rootSources = @(Get-ChildItem -LiteralPath $projectRoot -File -Filter '*.cs')
    if ($rootSources.Count -ne 0) {
        throw "Source files must be owned by a Core, Infrastructure, Modules, Features, Shared, UI, or Tests directory. Root files: $($rootSources.Name -join ', ')"
    }

    $sourceFiles = @(Get-ChildItem -LiteralPath $projectRoot -Recurse -File -Filter '*.cs' |
        Where-Object {
            $_.FullName -notmatch '\\(bin|obj|Artifacts|SourceSnapshots)\\'
        })
    $oversized = @($sourceFiles | ForEach-Object {
        $lineCount = (Get-Content -LiteralPath $_.FullName).Count
        if ($lineCount -gt $MaximumSourceFileLines) {
            [pscustomobject]@{ Path = $_.FullName; Lines = $lineCount }
        }
    })
    if ($oversized.Count -ne 0) {
        $details = ($oversized | ForEach-Object { "$($_.Lines) $($_.Path)" }) -join [Environment]::NewLine
        throw "Source responsibility limit exceeded:$([Environment]::NewLine)$details"
    }

    $patchCount = @($sourceFiles | Select-String -Pattern '\[HarmonyPatch' -Encoding UTF8).Count
    if ($patchCount -ne $ExpectedHarmonyPatchTypes) {
        throw "Harmony patch manifest count changed: $patchCount/$ExpectedHarmonyPatchTypes. Review the patch manifest before accepting the change."
    }

    & (Join-Path $PSScriptRoot 'Verify-Localization.ps1') -ProjectRoot $projectRoot

    dotnet format .\ElinModifier.csproj whitespace --no-restore --verify-no-changes --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Formatting verification failed.' }

    dotnet build .\ElinModifier.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    dotnet run --project .\Tests\ElinModifier.CoreTests.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Core regression tests failed.' }

    Write-Host "Structure verification passed: $($sourceFiles.Count) source files, no file over $MaximumSourceFileLines lines, $patchCount Harmony patches."
}
finally {
    Pop-Location
}
