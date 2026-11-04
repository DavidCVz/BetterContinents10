param([Parameter(Mandatory)][string]$Mono,[Parameter(Mandatory)][string]$Bootstrap,[Parameter(Mandatory)][string]$Reference,[Parameter(Mandatory)][string]$Output,[Parameter(Mandatory)][ValidateSet('Continents','Pangaea','Swirls','Vanillaish')][string]$Preset,[Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9]{1,10}$')][string]$Seed,[ValidateRange(64,2048)][int]$Resolution=1024)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Output){throw 'Output must be new.'}
$Output=[IO.Path]::GetFullPath($Output);$Reference=[IO.Path]::GetFullPath($Reference);$Bootstrap=[IO.Path]::GetFullPath($Bootstrap);$Mono=[IO.Path]::GetFullPath($Mono)
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$m=Get-Content (Join-Path $Reference 'manifest.json') -Raw|ConvertFrom-Json
if($m.captureVersion -ne '0.1.5'){throw 'A verified fractional reference is required.'}
New-Item -ItemType Directory -Path $Output|Out-Null
# Full pilot gate checks runtime/bootstrap/build/reference hashes and sector/native/fractional parity.
& (Join-Path $PSScriptRoot 'Run-TerrainPilot.ps1') -Mono $Mono -Bootstrap $Bootstrap -Reference $Reference -Output (Join-Path $Output 'pilot-validation') *> (Join-Path $Output 'pilot-validation.log')
$library=[IO.Path]::GetFullPath((Join-Path (Split-Path $Mono) '../lib/mono/4.5'))
$old=$env:MONO_PATH;$env:MONO_PATH=$library+';'+(Join-Path $library 'Facades')
try{
    $map=Join-Path $Output 'map';$worker=Join-Path $PSScriptRoot 'bin/Release/net48/MonoV3Probe.exe'
    & $Mono '--optimize=-float32' $worker $repo $Bootstrap $Preset $Seed $map $Reference $Resolution --terrain-map *> (Join-Path $Output 'map.log')
    if($LASTEXITCODE){throw 'Terrain map generation or native vertex audit failed.'}
    & dotnet (Join-Path $repo 'tools/seed-previewer/bin/Release/net10.0/SeedPreviewer.dll') --render-terrain $map *> (Join-Path $Output 'render.log')
    if($LASTEXITCODE){throw 'Terrain rendering failed.'}
    $files=@(Get-ChildItem -LiteralPath $map -File|ForEach-Object {[pscustomobject]@{File=$_.Name;Sha256=(Get-FileHash $_.FullName).Hash}})
    [pscustomobject]@{Status='offline-terrain-map-not-global-game-parity';Preset=$Preset;Seed=$Seed;Resolution=$Resolution;ReferenceManifestSha256=(Get-FileHash (Join-Path $Reference 'manifest.json')).Hash;WorkerSha256=(Get-FileHash $worker).Hash;MonoSha256=(Get-FileHash $Mono).Hash;PilotValidationSha256=(Get-FileHash (Join-Path $Output 'pilot-validation/terrain-run.json')).Hash;Files=$files}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $Output 'manifest.json')
}finally{$env:MONO_PATH=$old}
