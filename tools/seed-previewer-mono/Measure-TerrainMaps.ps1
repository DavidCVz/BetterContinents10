param([Parameter(Mandatory)][string]$Mono,[Parameter(Mandatory)][string]$Bootstrap,[Parameter(Mandatory)][string]$PilotReference,[Parameter(Mandatory)][string]$Output,[switch]$SmokeTest)
$ErrorActionPreference='Stop'
if(Get-Process Valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before benchmarking.'}
if(Test-Path -LiteralPath $Output){throw 'Output must be new.'}
$Output=[IO.Path]::GetFullPath($Output);$Mono=[IO.Path]::GetFullPath($Mono);$Bootstrap=[IO.Path]::GetFullPath($Bootstrap);$PilotReference=[IO.Path]::GetFullPath($PilotReference)
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));New-Item -ItemType Directory -Path $Output|Out-Null
# Gate once outside timed runs. Each timed worker is a fresh process.
& (Join-Path $PSScriptRoot 'Run-TerrainPilot.ps1') -Mono $Mono -Bootstrap $Bootstrap -Reference $PilotReference -Output (Join-Path $Output 'preflight') *> (Join-Path $Output 'preflight.log')
$hardwareComplete=$true
try{$cpu=Get-CimInstance Win32_Processor|Select-Object Name,NumberOfCores,NumberOfLogicalProcessors;$system=Get-CimInstance Win32_ComputerSystem|Select-Object TotalPhysicalMemory}
catch{$hardwareComplete=$false;$cpu=$env:PROCESSOR_IDENTIFIER;$system=$null}
[pscustomobject]@{CPU=$cpu;Memory=$system;HardwareComplete=$hardwareComplete;OS=[Environment]::OSVersion.VersionString;LogicalProcessors=[Environment]::ProcessorCount;Conditions='Sequential fresh workers, Valheim closed; OS file caches not flushed; native audit included in initialization.';WorkerSha256=(Get-FileHash (Join-Path $PSScriptRoot 'bin/Release/net48/MonoV3Probe.exe')).Hash;RendererSha256=(Get-FileHash (Join-Path $repo 'tools/seed-previewer/bin/Release/net10.0/SeedPreviewer.dll')).Hash;MonoSha256=(Get-FileHash $Mono).Hash}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $Output 'environment.local.json')
if(!$hardwareComplete -and !$SmokeTest){throw 'Full benchmark requires CPU/RAM read access; hardware inventory is incomplete.'}
function Measure-Child([string]$Executable,[string[]]$Arguments,[string]$Log,[bool]$IsMono){
    $start=[Diagnostics.ProcessStartInfo]::new($Executable);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.WorkingDirectory=$repo
    foreach($arg in $Arguments){$start.ArgumentList.Add($arg)}
    if($IsMono){$library=[IO.Path]::GetFullPath((Join-Path (Split-Path $Mono) '../lib/mono/4.5'));$start.Environment['MONO_PATH']=$library+';'+(Join-Path $library 'Facades')}
    $watch=[Diagnostics.Stopwatch]::StartNew();$p=[Diagnostics.Process]::Start($start);$stdout=$p.StandardOutput.ReadToEndAsync();$stderr=$p.StandardError.ReadToEndAsync();$peak=0L
    try{
        while(!$p.WaitForExit(100)){try{$p.Refresh();$peak=[math]::Max($peak,$p.PeakWorkingSet64)}catch{if(!$p.HasExited){throw}}}
        try{$peak=[math]::Max($peak,$p.PeakWorkingSet64)}catch{}
        [IO.File]::WriteAllText($Log,$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult())
        if($p.ExitCode){throw 'Benchmark child failed; inspect its log.'}
        [pscustomobject]@{ElapsedMs=$watch.ElapsedMilliseconds;PeakWorkingSetBytes=$peak;PeakMethod='OS peak working set queried every 100ms and at exit when available'}
    }finally{$p.Dispose()}
}
$results=@();$slot=0
$presets=@('Continents','Pangaea','Swirls','Vanillaish');$seeds=@('AbC123','abc123','North2026');$repetitions=1,2,3;$resolution='1024'
if($SmokeTest){$presets=@('Continents');$seeds=@('AbC123');$repetitions=@(1);$resolution='64'}
foreach($preset in $presets){foreach($seed in $seeds){$slot++
    foreach($run in $repetitions){
        if(Get-Process Valheim -ErrorAction SilentlyContinue){throw 'Valheim started during benchmark.'}
        $folder=Join-Path $Output "case$slot-run$run";New-Item -ItemType Directory -Path $folder|Out-Null;$map=Join-Path $folder 'map'
        $worker=Measure-Child $Mono @('--optimize=-float32',(Join-Path $PSScriptRoot 'bin/Release/net48/MonoV3Probe.exe'),$repo,$Bootstrap,$preset,$seed,$map,$PilotReference,$resolution,'--terrain-map') (Join-Path $folder 'worker.log') $true
        $render=Measure-Child (Get-Command dotnet).Source @((Join-Path $repo 'tools/seed-previewer/bin/Release/net10.0/SeedPreviewer.dll'),'--render-terrain',$map) (Join-Path $folder 'render.log') $false
        $m=Get-Content (Join-Path $map 'map.json') -Raw|ConvertFrom-Json
        $results+=[pscustomobject]@{Preset=$preset;Seed=$seed;Run=$run;Resolution=$resolution;ConfigurationMs=$m.configurationMs;InitializationAndAuditMs=$m.initializationMs;SamplingMs=$m.samplingMs;BinaryExportMs=$m.binaryExportMs;Worker=$worker;PngExport=$render;TotalMs=$worker.ElapsedMs+$render.ElapsedMs;SurfaceSha256=(Get-FileHash (Join-Path $map 'surface.f32')).Hash}
        $results|ConvertTo-Json -Depth 7|Set-Content (Join-Path $Output 'runs.json');Write-Output "Measured $preset / $seed / $run"
    }
}}
if($SmokeTest){[pscustomobject]@{Status='smoke-only-not-CA08';Runs=$results}|ConvertTo-Json -Depth 7|Set-Content (Join-Path $Output 'summary.json');return}
$summary=@($results|Group-Object Preset,Seed -CaseSensitive|ForEach-Object {$runs=@($_.Group);$times=@($runs.TotalMs|Sort-Object);if(@($runs.SurfaceSha256|Sort-Object -Unique).Count -ne 1){throw 'Benchmark repetitions differ.'};[pscustomobject]@{Preset=$runs[0].Preset;Seed=$runs[0].Seed;Runs=$runs.Count;TotalMedianMs=$times[1];TotalMinMs=$times[0];TotalMaxMs=$times[2];WorkerPeakBytes=($runs.Worker.PeakWorkingSetBytes|Measure-Object -Maximum).Maximum;RendererPeakBytes=($runs.PngExport.PeakWorkingSetBytes|Measure-Object -Maximum).Maximum}})
if($summary.Count -ne 12 -or @($summary|Where-Object Runs -ne 3).Count){throw 'Incomplete benchmark groups.'}
$summary|ConvertTo-Json -Depth 5|Set-Content (Join-Path $Output 'summary.json')
