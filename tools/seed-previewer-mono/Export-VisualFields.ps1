param([Parameter(Mandatory)][string]$Mono,[Parameter(Mandatory)][string]$Bootstrap,[Parameter(Mandatory)][string]$Reference,[Parameter(Mandatory)][string]$Comparison,[Parameter(Mandatory)][string]$Bundle,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
foreach($name in @('Mono','Bootstrap','Reference','Comparison','Bundle')){Set-Variable -Name $name -Value (Resolve-Path -LiteralPath (Get-Variable -Name $name -ValueOnly)).Path}
$Output=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $Output){throw 'Output must be new.'}
if($Output.StartsWith($Bundle.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Visual output must be outside M1 bundle.'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));$worker=Join-Path $PSScriptRoot 'bin/Release/net48/MonoV3Probe.exe'
$proof=Get-Content (Join-Path $Comparison 'comparison/visual-comparison.json') -Raw|ConvertFrom-Json
$gate=Get-Content (Join-Path $Comparison 'pilot-validation/terrain-run.json') -Raw|ConvertFrom-Json
if($proof.status -cne 'point-comparison-passed-not-raster-acceptance' -or $proof.differences -ne 0 -or $proof.values -lt 3200 -or $proof.workerSha256 -cne (Get-FileHash $worker).Hash -or $proof.referenceManifestSha256 -cne (Get-FileHash (Join-Path $Reference 'manifest.json')).Hash){throw 'A successful visual comparison by this worker is required.'}
function CheckReference {
 if($proof.referenceManifestSha256 -cne (Get-FileHash (Join-Path $Reference 'manifest.json')).Hash){throw 'Visual reference manifest changed.'}
 $native=Get-Content (Join-Path $Reference 'manifest.json') -Raw|ConvertFrom-Json
 foreach($pair in @(@('visual-points.json','pointsSha256'),@('biome-context.json','contextSha256'),@('effective-settings.txt','settingsSha256'))){if((Get-FileHash -LiteralPath (Join-Path $Reference $pair[0])).Hash -cne $native.($pair[1])){throw 'Visual reference source changed.'}}
}
CheckReference
if($gate.Status -cne 'pilot-and-sectors-complete-not-global-terrain-parity' -or $gate.MonoSha256 -cne (Get-FileHash $Mono).Hash){throw 'Runtime proof mismatch.'}
$records=@(Get-Content (Join-Path $Bootstrap 'terrain-bootstrap.json') -Raw|ConvertFrom-Json)
if(($records|ConvertTo-Json -Compress) -cne ($gate.Bootstrap|ConvertTo-Json -Compress)){throw 'Bootstrap proof mismatch.'}
foreach($r in $records){if($r.File -notin @('BetterContinents.dll','assembly_utils.dll','UnityEngine.CoreModule.dll','assembly_valheim.dll','0Harmony.dll') -or (Get-FileHash (Join-Path $Bootstrap $r.File)).Hash -cne $r.Sha256){throw 'Changed bootstrap.'}}
$m=Get-Content (Join-Path $Bundle 'manifest.json') -Raw|ConvertFrom-Json
$library=[IO.Path]::GetFullPath((Join-Path (Split-Path $Mono) '../lib/mono/4.5'))
$old=$env:MONO_PATH;$env:MONO_PATH=$library+';'+(Join-Path $library 'Facades')
try{
 & $Mono '--optimize=-float32' $worker $repo $Bootstrap $m.Preset $m.Seed $Output $Reference $m.Resolution --visual-map $Bundle
 if($LASTEXITCODE){throw 'Visual field export failed.'}
 CheckReference
 $v=Get-Content (Join-Path $Output 'visual-manifest.json') -Raw|ConvertFrom-Json
 $v|Add-Member -NotePropertyName referenceManifestSha256 -NotePropertyValue $proof.referenceManifestSha256
 $v|Add-Member -NotePropertyName comparisonSha256 -NotePropertyValue (Get-FileHash (Join-Path $Comparison 'comparison/visual-comparison.json')).Hash
 $v|Add-Member -NotePropertyName monoSha256 -NotePropertyValue $gate.MonoSha256
 $v|ConvertTo-Json -Depth 20|Set-Content (Join-Path $Output 'visual-manifest.json') -Encoding utf8
}finally{$env:MONO_PATH=$old}
