param([Parameter(Mandatory)][string]$Mono,[Parameter(Mandatory)][string]$Bootstrap,[Parameter(Mandatory)][string]$Reference,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Output){throw 'Matrix output must be new.'}
$Output=[IO.Path]::GetFullPath($Output);$Mono=[IO.Path]::GetFullPath($Mono);$Bootstrap=[IO.Path]::GetFullPath($Bootstrap);$Reference=[IO.Path]::GetFullPath($Reference)
New-Item -ItemType Directory -Path $Output|Out-Null
$results=@();$seeds=@('AbC123','abc123','North2026')
foreach($preset in @('Continents','Pangaea','Swirls','Vanillaish')){
    for($index=0;$index -lt $seeds.Count;$index++){
        $seed=$seeds[$index];$pair="$preset-s$($index+1)";$jobs=@()
        try{
            # Independent OS processes isolate MONO_PATH. Numbered seed slots also
            # avoid collisions between AbC123 and abc123 on Windows filesystems.
            foreach($run in 1,2){
                $destination=Join-Path $Output "$pair-run$run"
                $jobs+=Start-Job -ScriptBlock {
                    param($script,$mono,$bootstrap,$reference,$output,$preset,$seed)
                    $ErrorActionPreference='Stop'
                    & $script -Mono $mono -Bootstrap $bootstrap -Reference $reference -Output $output -Preset $preset -Seed $seed -Resolution 1024
                } -ArgumentList (Join-Path $PSScriptRoot 'Run-TerrainMap.ps1'),$Mono,$Bootstrap,$Reference,$destination,$preset,$seed
            }
            while(@($jobs|Where-Object State -in @('Running','NotStarted')).Count){$jobs|Wait-Job -Timeout 30|Out-Null}
            foreach($job in $jobs){Receive-Job $job -ErrorAction Stop;if($job.State -ne 'Completed'){throw 'Terrain map worker failed.'}}
        }finally{$jobs|Stop-Job -ErrorAction SilentlyContinue;$jobs|Remove-Job -Force -ErrorAction SilentlyContinue}
        $first=$null
        foreach($run in 1,2){
            $folder=Join-Path $Output "$pair-run$run/map"
            $data=Get-Content (Join-Path $folder 'map.json') -Raw|ConvertFrom-Json
            $audit=Get-Content (Join-Path $folder 'sparse-comparison.json') -Raw|ConvertFrom-Json
            $hashes=[ordered]@{}
            foreach($file in @('surface.f32','biomes.i32','below-ocean.u8','surface.png','biomes.png','below-ocean.png','world-sectors.json','effective-settings.txt')){$hashes[$file]=(Get-FileHash (Join-Path $folder $file)).Hash}
            if($run -eq 1){$first=$hashes}else{foreach($file in $hashes.Keys){if($hashes[$file] -cne $first[$file]){throw "Repeat mismatch: $pair/$file"}}}
            $results+=[pscustomobject]@{Preset=$preset;Seed=$seed;Slot=$index+1;Run=$run;Resolution=1024;NativeVertices=$audit.points;NativeDifferences=$audit.differences;InitializationMs=$data.initializationMs;SamplingMs=$data.samplingMs;SectorCount=$data.sectorCount;ModifiedSectors=$data.modifiedSectors;Hashes=$hashes}
        }
        $results|ConvertTo-Json -Depth 6|Set-Content (Join-Path $Output 'progress.json')
        Write-Output "PASS $pair ($seed): two independent 1024 maps match."
    }
}
$surfaceHashes=@($results|Where-Object Run -eq 1|ForEach-Object {$_.Hashes['surface.f32']}|Sort-Object -Unique)
if($surfaceHashes.Count -ne 12){throw 'Unexpected duplicate surface maps across matrix inputs.'}
[pscustomobject]@{Status='12-offline-maps-repeated-not-global-game-parity';AllPairsIdentical=$true;DistinctSurfaceMaps=$surfaceHashes.Count;Runs=$results}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $Output 'validation.json')
