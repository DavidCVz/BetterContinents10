param([Parameter(Mandatory)][string]$Mono,[Parameter(Mandatory)][string]$Bootstrap,[Parameter(Mandatory)][string]$Reference,[Parameter(Mandatory)][string]$Comparison,[Parameter(Mandatory)][string]$Pilot,[Parameter(Mandatory)][string]$Matrix,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
foreach($name in @('Mono','Bootstrap','Reference','Comparison','Pilot','Matrix')){Set-Variable -Name $name -Value (Resolve-Path -LiteralPath (Get-Variable -Name $name -ValueOnly)).Path}
$Output=[IO.Path]::GetFullPath($Output);if(Test-Path -LiteralPath $Output){throw 'Verification output must be new.'}
$null=New-Item -ItemType Directory -Path $Output
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));$worker=Join-Path $PSScriptRoot 'bin/Release/net48/MonoV3Probe.exe'
$library=[IO.Path]::GetFullPath((Join-Path (Split-Path $Mono) '../lib/mono/4.5'))
$results=@();$seeds=@('AbC123','abc123','North2026');$old=$env:MONO_PATH;$env:MONO_PATH=$library+';'+(Join-Path $library 'Facades')
try{
 foreach($preset in @('Continents','Pangaea','Swirls','Vanillaish')){for($slot=1;$slot -le 3;$slot++){
    $name="$preset-s$slot";$bundle=Join-Path $Matrix "$name-run1"
    # Export performs runtime/bootstrap/comparison admission before the legacy route is exercised.
    & (Join-Path $PSScriptRoot 'Export-VisualFields.ps1') -Mono $Mono -Bootstrap $Bootstrap -Reference $Reference -Comparison $Comparison -Bundle $bundle -Output (Join-Path $Output "$name-fields") *> (Join-Path $Output "$name-fields.log")
    & $Mono '--optimize=-float32' $worker $repo $Bootstrap $preset $seeds[$slot-1] (Join-Path $Output "$name-regression") $Pilot 1024 --terrain-map *> (Join-Path $Output "$name-regression.log")
    if($LASTEXITCODE){throw "M1 regression generation failed: $name"}
    $hashes=[ordered]@{}
    foreach($file in @('surface.f32','biomes.i32','below-ocean.u8')){
       $hashes[$file]=(Get-FileHash (Join-Path $Output "$name-regression/$file")).Hash
       if($hashes[$file] -cne (Get-FileHash (Join-Path $bundle "map/$file")).Hash){throw "M1 regression hash differs: $name/$file"}
    }
    $audit=Get-Content (Join-Path $Output "$name-regression/sparse-comparison.json") -Raw|ConvertFrom-Json
    if($audit.differences -ne 0){throw 'Native/sparse vertex audit failed.'}
    $v=Get-Content (Join-Path $Output "$name-fields/visual-manifest.json") -Raw|ConvertFrom-Json
    $results+=[pscustomobject]@{case=$name;seed=$seeds[$slot-1];rawM1Hashes=$hashes;visualFiles=$v.files;classes=$v.classes.Count;variantPixels=$v.variantPixels;samplingMs=$v.samplingMs;nativeVertices=$audit.points}
    $results|ConvertTo-Json -Depth 8|Set-Content (Join-Path $Output 'progress.json') -Encoding utf8
    Write-Output "PASS $name : three M1 hashes exact; visual controls exported."
 }}
 [ordered]@{status='passed';cases=$results;all36M1LayerHashesExact=$true;workerSha256=(Get-FileHash $worker).Hash;comparisonSha256=(Get-FileHash (Join-Path $Comparison 'comparison/visual-comparison.json')).Hash}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $Output 'validation.json') -Encoding utf8
}finally{$env:MONO_PATH=$old}
