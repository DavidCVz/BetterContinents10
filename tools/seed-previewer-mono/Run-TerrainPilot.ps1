param([Parameter(Mandatory)][string]$Mono,[Parameter(Mandatory)][string]$Bootstrap,[Parameter(Mandatory)][string]$Reference,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Output){throw 'Output must be new.'}
$m=Get-Content (Join-Path $Reference 'manifest.json') -Raw|ConvertFrom-Json
if($m.captureVersion -ne '0.1.4'){throw 'Terrain sector validation requires capture 0.1.4 with the authored catalog.'}
$Output=[IO.Path]::GetFullPath($Output);$Bootstrap=[IO.Path]::GetFullPath($Bootstrap)
$Mono=[IO.Path]::GetFullPath($Mono);$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$bytes=[IO.File]::ReadAllBytes($Mono);$pe=[BitConverter]::ToInt32($bytes,60)
if([BitConverter]::ToUInt16($bytes,$pe+4) -ne 0x8664){throw 'Terrain pilot requires the tested x64 Mono executable.'}
$records=@(Get-Content (Join-Path $Bootstrap 'terrain-bootstrap.json') -Raw|ConvertFrom-Json)
foreach($r in $records){if($r.File -notin @('BetterContinents.dll','assembly_utils.dll','UnityEngine.CoreModule.dll','assembly_valheim.dll','0Harmony.dll') -or (Get-FileHash (Join-Path $Bootstrap $r.File)).Hash -ne $r.Sha256){throw 'Terrain bootstrap identity mismatch.'}}
if($records.Count -ne 5 -or @($records.File|Sort-Object -Unique).Count -ne 5){throw 'Prepare a new terrain bootstrap including Harmony identity.'}
$library=[IO.Path]::GetFullPath((Join-Path (Split-Path $Mono) '../lib/mono/4.5'))
if(!(Test-Path (Join-Path $library 'mscorlib.dll'))){throw 'Mono class libraries missing.'}
$old=$env:MONO_PATH;$env:MONO_PATH=$library+';'+(Join-Path $library 'Facades')
try {
    # Native fixture verification checks capture integrity/build before starting terrain.
    & (Join-Path $PSScriptRoot 'Verify-Native.ps1') -Mono $Mono -Reference $Reference -Output $Output
    $m=Get-Content (Join-Path $Reference 'manifest.json') -Raw|ConvertFrom-Json
    if($m.seed -cne 'AbC123' -or $m.numericSeed -ne 874404622){throw 'Only the matching Continents/AbC123 pilot is supported.'}
    if((@($m.plugins.guid|Sort-Object)-join ',') -ne 'BetterContinents,local.seedpreviewer.reference'){throw 'Unexpected reference plugins.'}
    $worker=Join-Path $PSScriptRoot 'bin/Release/net48/MonoV3Probe.exe'
    & $Mono '--optimize=-float32' $worker --verify-gap $repo $Bootstrap (Join-Path $Output 'gap-check') HarmonyX *> (Join-Path $Output 'gap-check.log')
    if($LASTEXITCODE){throw 'Gap ABI compatibility check failed; terrain not started.'}
    & $Mono '--optimize=-float32' $worker $repo $Bootstrap Continents AbC123 (Join-Path $Output 'terrain') ([IO.Path]::GetFullPath($Reference)) 64 --terrain-pilot *> (Join-Path $Output 'terrain.log')
    if($LASTEXITCODE){throw 'Terrain pilot failed or differs; inspect diagnostics.'}
    $expected=(Get-Content (Join-Path $Reference 'effective-settings.txt') -Raw).Replace("`r`n","`n").Trim()
    $actual=(Get-Content (Join-Path $Output 'terrain/effective-settings.txt') -Raw).Replace("`r`n","`n").Trim()
    if($actual -cne $expected){throw 'Effective settings mismatch; pilot not accepted.'}
    [pscustomobject]@{Status='pilot-and-sectors-complete-not-global-terrain-parity';MonoSha256=(Get-FileHash $Mono).Hash;WorkerSha256=(Get-FileHash $worker).Hash;JsonReaderSha256=(Get-FileHash (Join-Path $repo 'tools/seed-previewer/.local/libs/Newtonsoft.Json.dll')).Hash;Options='--optimize=-float32';Bootstrap=$records;ReferenceManifestSha256=(Get-FileHash (Join-Path $Reference 'manifest.json')).Hash;BiomeContextSha256=$m.biomeContextSha256;SectorOutputSha256=(Get-FileHash (Join-Path $Output 'terrain/world-sectors.json')).Hash;SectorComparisonSha256=(Get-FileHash (Join-Path $Output 'terrain/sector-comparison.json')).Hash;DebugBuildFlag=$false;Build='Actual sector generation with authored catalog, then HeightmapBuilder.Build synchronously; no cache/save wrapper or background builder thread'}|ConvertTo-Json -Depth 7|Set-Content (Join-Path $Output 'terrain-run.json')
    Get-Content (Join-Path $Output 'terrain/terrain-comparison.json')
} finally {$env:MONO_PATH=$old}
