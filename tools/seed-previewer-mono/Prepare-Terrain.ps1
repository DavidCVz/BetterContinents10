param([Parameter(Mandatory)][string]$BaseBootstrap,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Output){throw 'Output must be new.'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));$Output=[IO.Path]::GetFullPath($Output)
$libs=Join-Path $repo 'tools/seed-previewer/.local/libs'
$baseRecords=@(Get-Content (Join-Path $BaseBootstrap 'bootstrap.json') -Raw|ConvertFrom-Json)
foreach($r in $baseRecords){
  $original=if($r.File -eq 'BetterContinents.dll'){Join-Path $repo 'obj/Release/net4.8/BetterContinents.dll'}elseif($r.File -eq 'assembly_utils.dll'){Join-Path $libs 'assembly_utils.dll'}else{throw 'Unexpected base bootstrap file.'}
  if((Get-FileHash $original).Hash -ne $r.OriginalSha256 -or (Get-FileHash (Join-Path $BaseBootstrap $r.File)).Hash -ne $r.AdaptedSha256){throw 'Stale base bootstrap.'}
}
if($baseRecords.Count -ne 2){throw 'Incomplete base bootstrap.'}
$null=[Reflection.Assembly]::LoadFrom((Join-Path $libs 'Mono.Cecil.dll'))
$resolver=New-Object Mono.Cecil.DefaultAssemblyResolver
foreach($dir in @($libs,(Join-Path $repo 'bin/Release/net4.8'),(Join-Path $PSScriptRoot 'bin/Release/net48'))){$resolver.AddSearchDirectory($dir)}
$reader=New-Object Mono.Cecil.ReaderParameters;$reader.AssemblyResolver=$resolver
New-Item -ItemType Directory -Path $Output|Out-Null
Copy-Item -LiteralPath (Join-Path $BaseBootstrap 'BetterContinents.dll'),(Join-Path $BaseBootstrap 'assembly_utils.dll') -Destination $Output
$worker=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $PSScriptRoot 'bin/Release/net48/MonoV3Probe.exe'),$reader)
$bridge=$worker.MainModule.Types|Where-Object Name -eq 'NativeBridge'
$core=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $libs 'UnityEngine.CoreModule.dll'),$reader)
$map=@(@('UnityEngine.Mathf','PerlinNoise','Perlin'),@('UnityEngine.Random','InitState','InitState'),@('UnityEngine.Random','RandomRangeInt','RangeInt'),@('UnityEngine.Random','Range','RangeFloat'),@('UnityEngine.Random','get_value','Value'),@('UnityEngine.Random','get_state_Injected','GetState'),@('UnityEngine.Random','set_state_Injected','SetState'),@('UnityEngine.Debug','get_isDebugBuild','IsDebugBuild'))
foreach($entry in $map){
  $type=$core.MainModule.Types|Where-Object FullName -eq $entry[0]
  $methods=@($type.Methods|Where-Object { $_.Name -eq $entry[1] -and ($entry[2] -ne 'RangeFloat' -or $_.ReturnType.FullName -eq 'System.Single') })
  if($methods.Count -ne 1){throw 'Native call shape changed.'};$m=$methods[0]
  $target=$bridge.Methods|Where-Object Name -eq $entry[2];if(!$target){throw 'Bridge method missing.'}
  $m.ImplAttributes=[Mono.Cecil.MethodImplAttributes]::IL
  $m.Body=New-Object Mono.Cecil.Cil.MethodBody($m);$il=$m.Body.GetILProcessor()
  for($i=0;$i -lt $m.Parameters.Count;$i++){$il.Emit([Mono.Cecil.Cil.OpCodes]::Ldarg,$m.Parameters[$i])}
  $il.Emit([Mono.Cecil.Cil.OpCodes]::Call,$core.MainModule.ImportReference($target));$il.Emit([Mono.Cecil.Cil.OpCodes]::Ret)
}
$core.Write((Join-Path $Output 'UnityEngine.CoreModule.dll'));$core.Dispose();$worker.Dispose()
# Preserve game terrain formulas. HarmonyX is used under Mono, as in the game.
Copy-Item -LiteralPath (Join-Path $libs 'assembly_valheim.dll') -Destination $Output
Copy-Item -LiteralPath (Join-Path $libs '0Harmony.dll') -Destination $Output
@('BetterContinents.dll','assembly_utils.dll','UnityEngine.CoreModule.dll','assembly_valheim.dll','0Harmony.dll')|ForEach-Object {[pscustomobject]@{File=$_;Sha256=(Get-FileHash (Join-Path $Output $_)).Hash}}|ConvertTo-Json|Set-Content (Join-Path $Output 'terrain-bootstrap.json')
