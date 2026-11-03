param([Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(Test-Path -LiteralPath $Output){throw 'Output must be a new directory.'}
$Output=[IO.Path]::GetFullPath($Output)
$libs=Join-Path $repo 'tools/seed-previewer/.local/libs'
$null=[Reflection.Assembly]::LoadFrom((Join-Path $libs 'Mono.Cecil.dll'))
$resolver=New-Object Mono.Cecil.DefaultAssemblyResolver
foreach($path in @($libs,(Join-Path $repo 'obj/Release/net4.8'),(Join-Path $repo 'bin/Release/net4.8'))){$resolver.AddSearchDirectory($path)}
$reader=New-Object Mono.Cecil.ReaderParameters; $reader.AssemblyResolver=$resolver
New-Item -ItemType Directory -Path $Output | Out-Null
$records=@()
foreach($item in @(@((Join-Path $repo 'obj/Release/net4.8/BetterContinents.dll'),'BetterContinents.dll'),@((Join-Path $libs 'assembly_utils.dll'),'assembly_utils.dll'))){
  $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($item[0],$reader)
  try {
    if($item[1] -eq 'BetterContinents.dll'){
      $method=($a.MainModule.Types|Where-Object FullName -eq 'BetterContinents.WarpedNoise').Methods|Where-Object Name -eq 'GetNoise'
      $calls=@($method.Body.Instructions|Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Single FastNoiseLite::GetNoise(System.Single,System.Single)' })
      if($calls.Count -ne 1){throw 'Unexpected raw noise call shape.'}
      $method.Body.GetILProcessor().InsertAfter($calls[0],[Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Conv_R4))
    } else {
      $type=$a.MainModule.Types|Where-Object FullName -eq 'Utils'
      $method=$type.Methods|Where-Object Name -eq '.cctor'
      $calls=@($method.Body.Instructions|Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.String UnityEngine.Application::get_persistentDataPath()' })
      if($calls.Count -ne 1 -or $calls[0].Next.Operand.Name -ne 'persistantDataPath'){throw 'Unexpected Utils initializer.'}
      # Standalone bootstrap only: no save APIs are supported or called by this probe.
      $calls[0].OpCode=[Mono.Cecil.Cil.OpCodes]::Ldstr; $calls[0].Operand=''
      foreach($name in @('.cctor','Length','LerpStep','Clamp01')){
        foreach($m in @($type.Methods|Where-Object Name -eq $name)){
          Add-Content -LiteralPath (Join-Path $Output 'utils-audit.local.log') -Value ($m.FullName + [Environment]::NewLine + (($m.Body.Instructions|ForEach-Object ToString) -join [Environment]::NewLine))
        }
      }
    }
    $destination=Join-Path $Output $item[1]; $a.Write($destination)
    $records += [pscustomobject]@{File=$item[1];OriginalSha256=(Get-FileHash $item[0]).Hash;AdaptedSha256=(Get-FileHash $destination).Hash}
  } finally {$a.Dispose()}
}
$records|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Output 'bootstrap.json')
