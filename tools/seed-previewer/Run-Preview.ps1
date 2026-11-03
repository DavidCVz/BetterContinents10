param(
    [Parameter(Mandatory)][string]$Mono,
    [Parameter(Mandatory)][string]$Bootstrap,
    [Parameter(Mandatory)][ValidateSet('Continents','Pangaea','Swirls','Vanillaish')][string]$Preset,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9]{1,10}$')][string]$Seed,
    [Parameter(Mandatory)][string]$Output,
    [ValidateRange(64,2048)][int]$Resolution=1024,
    [string]$Reference
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$Bootstrap=[IO.Path]::GetFullPath($Bootstrap);$Output=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $Output){throw 'Output must be new.'}
function Assert-Hash([string]$file,[string]$expected){if(!(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file).Hash -ne $expected){throw "Dependency/reference hash mismatch: $([IO.Path]::GetFileName($file))"}}
$runtime=Get-Content -LiteralPath (Join-Path $Bootstrap 'runtime.json') -Raw|ConvertFrom-Json
Assert-Hash $Mono $runtime.MonoSha256
foreach($name in @('assembly_valheim.dll','UnityEngine.CoreModule.dll','BepInEx.dll')){Assert-Hash (Join-Path $PSScriptRoot ".local/libs/$name") $runtime.Dependencies.$name}
Assert-Hash (Join-Path $repo 'bin/Release/net4.8/BetterContinents.dll') $runtime.DistributionSha256
$records=@(Get-Content -LiteralPath (Join-Path $Bootstrap 'bootstrap.json') -Raw|ConvertFrom-Json)
if($records.Count -ne 2 -or ((@($records.File|Sort-Object) -join ',') -ne 'assembly_utils.dll,BetterContinents.dll')){throw 'Unexpected bootstrap inventory.'}
foreach($r in $records){
    $original=if($r.File -eq 'BetterContinents.dll'){Join-Path $repo 'obj/Release/net4.8/BetterContinents.dll'}else{Join-Path $PSScriptRoot '.local/libs/assembly_utils.dll'}
    Assert-Hash $original $r.OriginalSha256;Assert-Hash (Join-Path $Bootstrap $r.File) $r.AdaptedSha256
}
$referenceArgument='-';$capture=$null
if($Reference){
    $Reference=[IO.Path]::GetFullPath($Reference);$referenceArgument=$Reference
    $capture=Get-Content -LiteralPath (Join-Path $Reference 'manifest.json') -Raw|ConvertFrom-Json
    if($capture.format -ne 'sp-reference-pilot/1' -or $capture.seed -cne $Seed -or $Preset -ne 'Continents'){throw 'Unsupported or mismatched reference identity.'}
    Assert-Hash (Join-Path $Reference 'points.tsv') $capture.pointsSha256
    Assert-Hash (Join-Path $Reference 'effective-settings.txt') $capture.settingsSha256
    Assert-Hash (Join-Path $PSScriptRoot '.local/libs/assembly_valheim.dll') $capture.gameSha256
    if($capture.modSha256 -ne $runtime.DistributionSha256){throw 'Reference distribution mismatch.'}
    if((@($capture.plugins.guid|Sort-Object) -join ',') -ne 'BetterContinents,local.seedpreviewer.reference'){throw 'Unexpected reference plugins.'}
    if($capture.precisionSha256){Assert-Hash (Join-Path $Reference 'precision-trace.json') $capture.precisionSha256}
    $rows=@(Import-Csv -LiteralPath (Join-Path $Reference 'points.tsv') -Delimiter "`t")
    if($rows.Count -ne 12675){throw 'Incomplete reference grid.'}
    $seen=@{}
    foreach($r in $rows){
        $zone="$($r.zoneX),$($r.zoneZ)";if($zone -notin @('0,0','16,-7','-83,59')){throw 'Unexpected zone.'}
        $x=[single]::Parse($r.x,[cultureinfo]::InvariantCulture);$z=[single]::Parse($r.z,[cultureinfo]::InvariantCulture)
        if($x -ne [math]::Truncate($x) -or $z -ne [math]::Truncate($z) -or [math]::Abs($x-64*[int]$r.zoneX) -gt 32 -or [math]::Abs($z-64*[int]$r.zoneZ) -gt 32){throw 'Unexpected coordinate.'}
        $key="$zone,$x,$z";if($seen.ContainsKey($key)){throw 'Duplicate reference point.'};$seen[$key]=$true
        foreach($field in @('x','z','baseHeightNormalized','surfaceMetres')){$v=[single]::Parse($r.$field,[cultureinfo]::InvariantCulture);if([single]::IsNaN($v)-or[single]::IsInfinity($v)){throw 'Non-finite reference.'}}
    }
    if(@($rows|Group-Object zoneX,zoneZ|Where-Object Count -ne 4225).Count){throw 'Incomplete zone.'}
}
$worker=Join-Path $repo 'tools/seed-previewer-mono/bin/Release/net48/MonoV3Probe.exe'
$renderer=Join-Path $PSScriptRoot 'bin/Release/net10.0/SeedPreviewer.dll'
if(!(Test-Path $worker) -or !(Test-Path $renderer)){throw 'Build both previewer projects first.'}
& $Mono $worker $repo $Bootstrap $Preset $Seed $Output $referenceArgument $Resolution
if($LASTEXITCODE -notin 0,2){throw 'Mono generation failed; no acceptance manifest.'}
$result=Get-Content -LiteralPath (Join-Path $Output 'generation.json') -Raw|ConvertFrom-Json
if($result.modSha256 -ne ($records|Where-Object File -eq 'BetterContinents.dll').AdaptedSha256 -or $result.utilsSha256 -ne ($records|Where-Object File -eq 'assembly_utils.dll').AdaptedSha256){throw 'Loaded assembly identity mismatch.'}
if($capture){
    if($result.numericSeed -ne $capture.numericSeed){throw 'Numeric seed mismatch.'}
    function Normalize([string]$value){$value.Replace("`r`n","`n").Trim()}
    if((Normalize (Get-Content (Join-Path $Output 'effective-settings.txt') -Raw)) -cne (Normalize (Get-Content (Join-Path $Reference 'effective-settings.txt') -Raw))){throw 'Effective settings mismatch.'}
    $actual=@(Import-Csv (Join-Path $Output 'pilot.tsv') -Delimiter "`t");$errors=@();$bitDifferences=0
    if($actual.Count -ne $rows.Count){throw 'Incomplete sampled pilot.'}
    for($i=0;$i -lt $rows.Count;$i++){
        if($actual[$i].x -ne $rows[$i].x -or $actual[$i].z -ne $rows[$i].z){throw 'Sample coordinate mismatch.'}
        $expected=[single]::Parse($rows[$i].baseHeightNormalized,[cultureinfo]::InvariantCulture)
        $bits=[BitConverter]::ToInt32([BitConverter]::GetBytes($expected),0).ToString('X8')
        $value=[BitConverter]::ToSingle([BitConverter]::GetBytes([Convert]::ToInt32($actual[$i].bits,16)),0)
        if([single]::IsNaN($value)-or[single]::IsInfinity($value)){throw 'Non-finite sampled pilot.'}
        if($bits -ne $actual[$i].bits){$bitDifferences++}
        $errors += [pscustomobject]@{Zone="$($rows[$i].zoneX),$($rows[$i].zoneZ)";ErrorMetres=([math]::Abs([double]$value-$expected)*200)}
    }
    if($bitDifferences -ne $result.pilotDifferences){throw 'Runtime parsers disagree on pilot comparison.'}
    [pscustomobject]@{Points=$rows.Count;BitDifferences=$bitDifferences;MaxErrorMetres=($errors.ErrorMetres|Measure-Object -Maximum).Maximum;MeanErrorMetres=($errors.ErrorMetres|Measure-Object -Average).Average;Zones=@($errors|Group-Object Zone|ForEach-Object {[pscustomobject]@{Zone=$_.Name;Points=$_.Count;MaxErrorMetres=($_.Group.ErrorMetres|Measure-Object -Maximum).Maximum;MeanErrorMetres=($_.Group.ErrorMetres|Measure-Object -Average).Average}})}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $Output 'reference-comparison.json')
}
$lifecycle=Get-Content (Join-Path $Output 'settings-lifecycle.json') -Raw|ConvertFrom-Json
foreach($field in $lifecycle.beforeFloats.PSObject.Properties){if($field.Value -ne $lifecycle.afterFloats.($field.Name)){throw 'Settings scalar changed during codec.'}}
if($lifecycle.presetVersion -ne 7 -or $lifecycle.loadedVersion -ne 11){throw 'Unexpected settings lifecycle.'}
Assert-Hash (Join-Path $Output 'height-base.f32') $result.dataSha256
& dotnet $renderer --render-base (Join-Path $Output 'height-base.f32') $Resolution (Join-Path $Output 'height-base.png')
if($LASTEXITCODE){throw 'Image export failed; no acceptance manifest.'}
$result.status=if($capture -and $result.pilotDifferences -eq 0){'base-height-pilot-exact-not-full-terrain-parity'}elseif($capture){'base-height-pilot-discrepancies'}else{'base-height-only-no-game-reference-for-this-combination'}
$manifest=[ordered]@{format='sp-base-preview/1';generation=$result;runtime='Mono';monoSha256=$runtime.MonoSha256;bootstrap=$records;distributionSha256=$runtime.DistributionSha256;gameSha256=(Get-FileHash (Join-Path $PSScriptRoot '.local/libs/assembly_valheim.dll')).Hash;presetSha256=(Get-FileHash (Join-Path $repo "assets/BCAssets/Presets/$Preset.BetterContinents")).Hash;imageSha256=(Get-FileHash (Join-Path $Output 'height-base.png')).Hash;coordinates='North-first row-major pixel centers; x=((col+0.5)/size-0.5)*total, z=(0.5-(row+0.5)/size)*total';totalSizeMetres=$result.totalSizeMetres;encoding='IEEE754 float32 little-endian; normalized base height; multiply by 200 for metres';image='16-bit grayscale normalized min..max, display only; not coastline';settingsSha256=(Get-FileHash (Join-Path $Output 'effective-settings.txt')).Hash;savedSettingsSha256=(Get-FileHash (Join-Path $Output 'saved-settings.bin')).Hash;precisionSha256=(Get-FileHash (Join-Path $Output 'precision-trace.json')).Hash;referencePointsSha256=if($capture){$capture.pointsSha256}else{$null}}
$manifest|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $Output 'manifest.json')
if($capture -and $result.pilotDifferences -ne 0){throw 'Pilot discrepancies remain; inspect manifest.'}
Write-Output "Base preview complete: $Preset/$Seed, $Resolution x $Resolution."
