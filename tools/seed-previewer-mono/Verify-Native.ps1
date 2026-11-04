param([Parameter(Mandatory)][string]$Mono,[Parameter(Mandatory)][string]$Reference,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Output){throw 'Output must be new.'}
$Output=[IO.Path]::GetFullPath($Output)
$m=Get-Content -LiteralPath (Join-Path $Reference 'manifest.json') -Raw|ConvertFrom-Json
if($m.format -ne 'sp-reference-pilot/1' -or $m.captureVersion -notin @('0.1.3','0.1.4')){throw 'Expected native capture 0.1.3 or 0.1.4.'}
if($m.captureVersion -eq '0.1.4' -and (Get-FileHash (Join-Path $Reference 'biome-context.json')).Hash -ne $m.biomeContextSha256){throw 'Biome context integrity failure.'}
foreach($entry in @(@('native-primitives.json','nativeSha256'),@('points.tsv','pointsSha256'),@('effective-settings.txt','settingsSha256'),@('precision-trace.json','precisionSha256'))){
    if((Get-FileHash -LiteralPath (Join-Path $Reference $entry[0])).Hash -ne $m.($entry[1])){throw 'Reference integrity failure.'}
}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if((Get-FileHash (Join-Path $repo 'tools/seed-previewer/.local/libs/assembly_valheim.dll')).Hash -ne $m.gameSha256 -or (Get-FileHash (Join-Path $repo 'bin/Release/net4.8/BetterContinents.dll')).Hash -ne $m.modSha256){throw 'Reference build mismatch.'}
$n=Get-Content -LiteralPath (Join-Path $Reference 'native-primitives.json') -Raw|ConvertFrom-Json
if($n.format -ne 'sp-native-reference/1' -or $n.perlin.Count -ne 625 -or $n.random.Count -ne 8 -or !$n.stateRestored){throw 'Incomplete native reference.'}
function Check-Bits($value){
    if($value.bits -notmatch '^[0-9A-F]{8}$'){throw 'Invalid float bits.'}
    $v=[BitConverter]::ToSingle([BitConverter]::GetBytes([Convert]::ToUInt32($value.bits,16)),0)
    if([single]::IsNaN($v)-or[single]::IsInfinity($v)){throw 'Non-finite reference.'}
    if([BitConverter]::ToInt32([BitConverter]::GetBytes([single]$value.value),0).ToString('X8') -ne $value.bits){throw 'Reference value/bit mismatch.'}
}
$seen=@{};foreach($p in $n.perlin){Check-Bits $p.x;Check-Bits $p.y;Check-Bits $p.value;$key="$($p.x.bits),$($p.y.bits)";if($seen.ContainsKey($key)){throw 'Duplicate Perlin coordinate.'};$seen[$key]=$true}
$initial=@();$steps=@();$seeds=@{}
foreach($r in $n.random){
    if($seeds.ContainsKey([string]$r.seed)-or$r.steps.Count -ne 128){throw 'Invalid RNG sequence.'};$seeds[[string]$r.seed]=$true
    $initial+=@($r.seed,$r.initial.s0,$r.initial.s1,$r.initial.s2,$r.initial.s3)-join "`t"
    for($i=0;$i -lt 128;$i++){
        $p=$r.steps[$i];if($p.operation -ne [math]::Floor($i/16)-or$p.index -ne ($i%16)){throw 'Operation order mismatch.'}
        if($p.operation -in 2,3){Check-Bits $p.number}
        foreach($word in @($r.initial.s0,$r.initial.s1,$r.initial.s2,$r.initial.s3,$p.stateAfter.s0,$p.stateAfter.s1,$p.stateAfter.s2,$p.stateAfter.s3)){if($word -notmatch '^[0-9A-F]{8}$'){throw 'Invalid RNG state word.'}}
        $steps+=@($r.seed,$p.operation,$p.index,$p.integer,$p.number.bits,$p.stateAfter.s0,$p.stateAfter.s1,$p.stateAfter.s2,$p.stateAfter.s3)-join "`t"
    }
}
Check-Bits $n.water.zoneSystemWaterLevelMetres
New-Item -ItemType Directory -Path $Output|Out-Null
$n.perlin|ForEach-Object {@($_.x.bits,$_.y.bits,$_.value.bits)-join "`t"}|Set-Content (Join-Path $Output 'perlin.tsv')
$initial|Set-Content (Join-Path $Output 'initial.tsv');$steps|Set-Content (Join-Path $Output 'random.tsv')
& $Mono (Join-Path $PSScriptRoot 'bin/Release/net48/MonoV3Probe.exe') --verify-native $Output (Join-Path $Output 'comparison.json')
if($LASTEXITCODE){throw 'Native candidate comparison failed.'}
[pscustomobject]@{ManifestSha256=(Get-FileHash (Join-Path $Reference 'manifest.json')).Hash;NativeSha256=$m.nativeSha256;MonoSha256=(Get-FileHash $Mono).Hash;WaterLevelMetres=$n.water.zoneSystemWaterLevelMetres.value;Limitation='Captured primitive domain only. No biome, surface or water-mask acceptance.'}|ConvertTo-Json|Set-Content (Join-Path $Output 'validation.json')
