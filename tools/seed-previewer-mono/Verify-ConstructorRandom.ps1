param([Parameter(Mandatory)][string]$Reference,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Output){throw 'Output must be new.'}
$manifest=Get-Content (Join-Path $Reference 'manifest.json') -Raw|ConvertFrom-Json
if($manifest.format -ne 'sp-reference-pilot/1' -or (Get-FileHash (Join-Path $Reference 'points.tsv')).Hash -ne $manifest.pointsSha256){throw 'Invalid reference.'}
if(!$('UnityIntegerRandomCandidate' -as [type])){Add-Type -Path @((Join-Path $PSScriptRoot 'UnityIntegerRandomCandidate.cs'),(Join-Path $PSScriptRoot 'NativeFloat.cs'))}
$results=@()
foreach($seed in @('874404622','874370830','30859693')){
    $expected=@($manifest.random.$seed);if($expected.Count -ne 7){throw 'Missing constructor sequence.'}
    $rng=[UnityIntegerRandomCandidate]::new([int]$seed);$actual=@()
    for($i=0;$i -lt 7;$i++){if($i -in 4,5){$actual+=$rng.Range([int]::MinValue,[int]::MaxValue)}else{$actual+=$rng.Range(-10000,10000)}}
    if(($actual -join ',') -ne ($expected -join ',')){throw "Constructor RNG mismatch for $seed"}
    $results+=[pscustomobject]@{Seed=$seed;Expected=$expected;Actual=$actual;Exact=$true}
}
[pscustomobject]@{Format='sp-constructor-rng-check/1';ReferenceManifestSha256=(Get-FileHash (Join-Path $Reference 'manifest.json')).Hash;Sequences=$results;Limitation='21 integer calls only. Does not validate state, float ranges or arbitrary sequences.'}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $Output
Write-Output 'PASS three constructor sequences, 21 exact integer outputs.'
