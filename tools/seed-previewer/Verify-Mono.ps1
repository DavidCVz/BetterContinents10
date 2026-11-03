param([Parameter(Mandatory)][string]$Mono,[Parameter(Mandatory)][string]$Bootstrap,[Parameter(Mandatory)][string]$Reference,[Parameter(Mandatory)][string]$Evidence)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Evidence){throw 'Evidence directory must be new.'}
$Evidence=[IO.Path]::GetFullPath($Evidence);New-Item -ItemType Directory -Path $Evidence|Out-Null
$entry=Join-Path $PSScriptRoot 'Run-Preview.ps1';$matrix=@();$seeds=@('AbC123','abc123','North2026');$expectedSeeds=@(874404622,874370830,30859693)
foreach($preset in @('Continents','Pangaea','Swirls','Vanillaish')){for($si=0;$si -lt 3;$si++){
    $pair=@()
    for($run=1;$run -le 2;$run++){
        $name="$preset-s$si-r$run";$output=Join-Path $Evidence $name
        $extra=@{};if($preset -eq 'Continents' -and $si -eq 0){$extra.Reference=$Reference}
        & $entry -Mono $Mono -Bootstrap $Bootstrap -Preset $preset -Seed $seeds[$si] -Output $output @extra *> (Join-Path $Evidence "$name.log")
        $m=Get-Content (Join-Path $output 'manifest.json') -Raw|ConvertFrom-Json
        if($m.generation.numericSeed -ne $expectedSeeds[$si] -or $m.generation.settingsVersion -ne 11 -or (Get-Item (Join-Path $output 'height-base.f32')).Length -ne 4194304){throw 'Unexpected generation metadata.'}
        $png=[IO.File]::ReadAllBytes((Join-Path $output 'height-base.png'))
        if($png[24] -ne 16 -or $png[25] -ne 0){throw 'Expected 16-bit grayscale PNG.'}
        if($extra.Count -and $m.generation.pilotDifferences -ne 0){throw 'Pilot mismatch.'}
        $pair+=$m
    }
    if($pair[0].generation.dataSha256 -ne $pair[1].generation.dataSha256 -or $pair[0].imageSha256 -ne $pair[1].imageSha256){throw 'Non-deterministic output.'}
    $matrix += [pscustomobject]@{Preset=$preset;Seed=$seeds[$si];Sha256=$pair[0].generation.dataSha256;ImageSha256=$pair[0].imageSha256;RepeatExact=$true}
    Write-Output "PASS $preset/$($seeds[$si])"
}}
if(@($matrix.Sha256|Sort-Object -Unique).Count -ne 12){throw 'Expected twelve distinct grids.'}
$matrix|ConvertTo-Json|Set-Content (Join-Path $Evidence 'Matrix.json')
$negative=@()
function Reject($name,$overrides,$pattern){
    $args=@{Mono=$Mono;Bootstrap=$Bootstrap;Preset='Continents';Seed='AbC123';Resolution=64;Output=(Join-Path $Evidence $name)}
    foreach($key in $overrides.Keys){$args[$key]=$overrides[$key]}
    $rejected=$false
    try{& $entry @args *> (Join-Path $Evidence "$name.log")}catch{if($_.Exception.Message -notmatch $pattern){throw};$rejected=$true}
    if(!$rejected){throw "Expected rejection: $name"}
    if($name -ne 'ExistingOutput' -and (Test-Path (Join-Path $args.Output 'manifest.json'))){throw 'Failed input left an acceptance manifest.'}
    $script:negative+=$name
}
Reject 'BadSeed' @{Seed='bad-seed'} 'pattern'
Reject 'BadSize' @{Resolution=63} '64'
Reject 'ExistingOutput' @{Output=(Join-Path $Evidence 'Continents-s0-r1')} 'new'
Reject 'WrongPresetReference' @{Preset='Swirls';Reference=$Reference} 'identity'
$bad=Join-Path $Evidence 'CorruptReference';New-Item -ItemType Directory -Path $bad|Out-Null;Copy-Item (Join-Path $Reference '*') $bad
Add-Content (Join-Path $bad 'points.tsv') 'corrupt'
Reject 'CorruptPoints' @{Reference=$bad} 'hash mismatch'
$changed=Join-Path $Evidence 'ChangedSettings';New-Item -ItemType Directory -Path $changed|Out-Null;Copy-Item (Join-Path $Reference '*') $changed
Add-Content (Join-Path $changed 'effective-settings.txt') 'incompatible settings'
$m=Get-Content (Join-Path $changed 'manifest.json') -Raw|ConvertFrom-Json;$m.settingsSha256=(Get-FileHash (Join-Path $changed 'effective-settings.txt')).Hash;$m|ConvertTo-Json -Depth 20|Set-Content (Join-Path $changed 'manifest.json')
Reject 'IncompatibleSettings' @{Reference=$changed} 'Effective settings mismatch'
$stale=Join-Path $Evidence 'StaleBootstrap';New-Item -ItemType Directory -Path $stale|Out-Null;Copy-Item (Join-Path $Bootstrap '*') $stale
Add-Content (Join-Path $stale 'BetterContinents.dll') 'corrupt'
Reject 'StaleAssembly' @{Bootstrap=$stale} 'hash mismatch'
$negative|ConvertTo-Json|Set-Content (Join-Path $Evidence 'Negative.json')
Write-Output 'PASS matrix, PNG format, reference validation, and negative inputs.'
