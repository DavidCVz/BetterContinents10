param([Parameter(Mandatory)][string]$Mono,[Parameter(Mandatory)][string]$Bootstrap,[Parameter(Mandatory)][string]$PilotReference,[Parameter(Mandatory)][string]$Reference,[Parameter(Mandatory)][ValidateSet('Continents','Pangaea','Swirls','Vanillaish')][string]$Preset,[Parameter(Mandatory)][ValidateSet('AbC123','abc123','North2026')][string]$Seed,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Output){throw 'Output must be new.'}
$Output=[IO.Path]::GetFullPath($Output);$Reference=[IO.Path]::GetFullPath($Reference);$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$m=Get-Content (Join-Path $Reference 'manifest.json') -Raw|ConvertFrom-Json
if($m.format -ne 'sp-reference-global/1' -or $m.captureVersion -notin @('0.2.0','0.2.1') -or $m.contract -ne 'unedited-nonlod-mesh-ocean30/1' -or $m.preset -cne $Preset -or $m.seed -cne $Seed -or $m.size -ne 256 -or $m.worldVersion -ne 2 -or $m.count -ne 65645 -or $m.coastCount -ne 50 -or $m.transitionCount -ne 50 -or $m.anchorCount -ne 9){throw 'Global reference format/identity mismatch.'}
if($m.captureVersion -eq '0.2.1' -and ($m.captureMode -notin @('selective-original-body','full-original-body') -or ($m.captureMode -eq 'selective-original-body' -and $m.selectiveChecks -ne 700))){throw 'Selective native preflight metadata missing.'}
if((@($m.plugins.guid|Sort-Object)-join ',') -ne 'BetterContinents,local.seedpreviewer.reference'){throw 'Unexpected global reference plugins.'}
foreach($e in @(@('points.tsv','pointsSha256'),@('effective-settings.txt','settingsSha256'),@('biome-context.json','biomeContextSha256'),@('mesh-triangles.i32','topologySha256'))){if((Get-FileHash (Join-Path $Reference $e[0])).Hash -ne $m.($e[1])){throw 'Global reference hash mismatch.'}}
if((Get-FileHash (Join-Path $repo 'tools/seed-previewer/.local/libs/assembly_valheim.dll')).Hash -ne $m.gameSha256 -or (Get-FileHash (Join-Path $repo 'bin/Release/net4.8/BetterContinents.dll')).Hash -ne $m.modSha256){throw 'Global reference build mismatch.'}
New-Item -ItemType Directory -Path $Output|Out-Null
& (Join-Path $PSScriptRoot 'Run-TerrainPilot.ps1') -Mono $Mono -Bootstrap $Bootstrap -Reference $PilotReference -Output (Join-Path $Output 'pilot-validation') *> (Join-Path $Output 'pilot-validation.log')
$library=[IO.Path]::GetFullPath((Join-Path (Split-Path $Mono) '../lib/mono/4.5'));$old=$env:MONO_PATH;$env:MONO_PATH=$library+';'+(Join-Path $library 'Facades')
try{
    $worker=Join-Path $PSScriptRoot 'bin/Release/net48/MonoV3Probe.exe'
    & $Mono '--optimize=-float32' $worker $repo ([IO.Path]::GetFullPath($Bootstrap)) $Preset $Seed (Join-Path $Output 'comparison') $Reference 256 --terrain-reference *> (Join-Path $Output 'comparison.log')
    if($LASTEXITCODE){throw 'Global comparison failed or differs; inspect comparison diagnostics.'}
    [pscustomobject]@{Status='one-global-combination-passed-not-matrix-acceptance';Preset=$Preset;Seed=$Seed;ReferenceManifestSha256=(Get-FileHash (Join-Path $Reference 'manifest.json')).Hash;PilotValidationSha256=(Get-FileHash (Join-Path $Output 'pilot-validation/terrain-run.json')).Hash;WorkerSha256=(Get-FileHash $worker).Hash;ComparisonSha256=(Get-FileHash (Join-Path $Output 'comparison/comparison.json')).Hash}|ConvertTo-Json|Set-Content (Join-Path $Output 'manifest.json')
}finally{$env:MONO_PATH=$old}
