param([Parameter(Mandatory)][string]$Mono,[Parameter(Mandatory)][string]$Bootstrap,[Parameter(Mandatory)][string]$Pilot,[Parameter(Mandatory)][string]$Reference,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$Reference=(Resolve-Path -LiteralPath $Reference).Path
$Output=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $Output){throw 'Output must be new.'}
$m=Get-Content (Join-Path $Reference 'manifest.json') -Raw|ConvertFrom-Json
$p=Get-Content (Join-Path $Pilot 'manifest.json') -Raw|ConvertFrom-Json
if($m.format -cne 'sp-visual-reference/1' -or $m.status -cne 'complete-native-observations-not-offline-parity' -or $m.seed -cne 'AbC123' -or $m.preset -cne 'Continents'){throw 'Expected the visual Continents/AbC123 capture.'}
if($m.gameSha256 -cne $p.gameSha256 -or $m.modSha256 -cne $p.modSha256){throw 'Visual and M1 pilot game/mod builds differ.'}
if((@($m.plugins.guid|Sort-Object)-join ',') -cne 'BetterContinents,local.seedpreviewer.reference'){throw 'Unexpected visual reference plugins.'}
foreach($pair in @(@('visual-points.json','pointsSha256'),@('biome-context.json','contextSha256'),@('effective-settings.txt','settingsSha256'))){if((Get-FileHash -LiteralPath (Join-Path $Reference $pair[0])).Hash -cne $m.($pair[1])){throw 'Visual reference integrity failed.'}}
$null=New-Item -ItemType Directory -Path $Output
# Existing gate validates x64 runtime/bootstrap, original builds and native/fractional M1 pilot.
& (Join-Path $PSScriptRoot 'Run-TerrainPilot.ps1') -Mono $Mono -Bootstrap $Bootstrap -Reference $Pilot -Output (Join-Path $Output 'pilot-validation') *> (Join-Path $Output 'pilot-validation.log')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$library=[IO.Path]::GetFullPath((Join-Path (Split-Path $Mono) '../lib/mono/4.5'))
$old=$env:MONO_PATH;$env:MONO_PATH=$library+';'+(Join-Path $library 'Facades')
try{
 & $Mono '--optimize=-float32' (Join-Path $PSScriptRoot 'bin/Release/net48/MonoV3Probe.exe') $repo ([IO.Path]::GetFullPath($Bootstrap)) Continents AbC123 (Join-Path $Output 'comparison') $Reference 64 --visual-reference *> (Join-Path $Output 'comparison.log')
 if($LASTEXITCODE){throw 'Visual comparison failed or differs; inspect comparison.log and visual-comparison.json.'}
 Get-Content (Join-Path $Output 'comparison/visual-comparison.json')
}finally{$env:MONO_PATH=$old}
