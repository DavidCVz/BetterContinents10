param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'EvidenceDirectory must be new.' }
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim before running offline verification.' }
New-Item -ItemType Directory $evidence | Out-Null
$probe = Join-Path $PSScriptRoot 'bin/Release/net10.0/SeedPreviewer.dll'
$common = @("$PSScriptRoot/.local/libs", "$repo/obj/Release/net4.8/BetterContinents.dll", "$repo/bin/Release/net4.8")
$rows = @()
foreach ($preset in @('Continents','Pangaea','Swirls','Vanillaish')) {
    $seedNumber = 0
    foreach ($seed in @('AbC123','abc123','North2026')) {
        $seedNumber++
        $manifests = @()
        foreach ($attempt in 1..2) {
            $name = "$preset-s$seedNumber-$seed-run$attempt"
            $output = Join-Path $evidence $name
            & dotnet $probe @common "$repo/assets/BCAssets/Presets/$preset.BetterContinents" --height $seed 1024 $output *> "$evidence/$name.log"
            if ($LASTEXITCODE -ne 0) { throw "Generation failed: $name. See its log." }
            $manifest = Get-Content "$output/manifest.json" -Raw | ConvertFrom-Json
            $lifecycle = Get-Content "$output/settings-lifecycle.json" -Raw | ConvertFrom-Json
            if ($manifest.presetVersion -ne 7 -or $manifest.settingsVersion -ne 11 -or $lifecycle.loadedAltBiomesStored) { throw "Unexpected loaded settings: $name" }
            foreach ($field in $lifecycle.beforeFloats.PSObject.Properties) {
                if ($field.Value.bits -ne $lifecycle.afterFloats.($field.Name).bits) { throw "Float changed during settings codec: $name/$($field.Name)" }
            }
            if ((Get-Item "$output/height-base.f32").Length -ne (1024 * 1024 * 4)) { throw "Invalid grid length: $name" }
            $actualHash = (Get-FileHash "$output/height-base.f32").Hash
            if ($actualHash -ne $manifest.dataSha256) { throw "Invalid manifest hash: $name" }
            $manifests += $manifest
        }
        if ($manifests[0].dataSha256 -ne $manifests[1].dataSha256) { throw "Non-deterministic grid: $preset/$seed" }
        $rows += [pscustomobject]@{Preset=$preset; Seed=$seed; NumericSeed=$manifests[0].numericSeed; LoadedSettingsVersion=$manifests[0].settingsVersion; Sha256=$manifests[0].dataSha256; RepeatIdentical=$true; Min=$manifests[0].min; Max=$manifests[0].max; ElapsedMs=$manifests[0].elapsedMs; PeakMemoryBytes=$manifests[0].peakMemoryBytes}
        Write-Output "PASS $preset / ${seed}: 1024 x 1024, identical across two processes"
    }
}
if (@($rows | Select-Object -ExpandProperty Sha256 -Unique).Count -ne 12) { throw 'Expected distinct grids for this fixture matrix.' }
$rows | ConvertTo-Json | Set-Content "$evidence/Matrix.json"
$negative = @()
foreach ($case in @(@('--height','AbC123','63',"$evidence/invalid-size"), @('--height','Bad!','64',"$evidence/invalid-seed"), @('--height','AbC123','64',"$evidence/Continents-s1-AbC123-run1"))) {
    $output = & dotnet $probe @common "$repo/assets/BCAssets/Presets/Continents.BetterContinents" @case 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 1) { throw "Negative case unexpectedly returned $exitCode" }
    $negative += "CASE $($case -join ' ') exit=$exitCode"
    $negative += $output | ForEach-Object { $_.ToString() }
}
$negative | Set-Content "$evidence/Negative.log"
& dotnet $probe @common "$repo/assets/BCAssets/Presets/Continents.BetterContinents" *> "$evidence/F01-regression.log"
if ($LASTEXITCODE -ne 0) { throw 'F01 read-only probe regressed.' }
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Valheim was started during the run.' }
'Valheim absent before and after; 24 generations; 12 identical pairs; 12 distinct grids; negative cases and F01 regression passed.' | Set-Content "$evidence/Summary.txt"
Write-Output (Get-Content "$evidence/Summary.txt")
