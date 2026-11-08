param([Parameter(Mandatory)][string]$Bundle,[Parameter(Mandatory)][string]$Fields,[Parameter(Mandatory)][string]$Output,[Parameter(Mandatory)][string]$OtherBundle)
$ErrorActionPreference='Stop'
$Output=[IO.Path]::GetFullPath($Output);if(Test-Path -LiteralPath $Output){throw 'New evidence directory required.'}
$null=New-Item -ItemType Directory -Path $Output
$cli=Join-Path $PSScriptRoot 'bin/Release/net10.0/SeedPreviewer.dll';$runs=@()
function Hashes($dir){$h=[ordered]@{};Get-ChildItem -LiteralPath $dir -File -Recurse|Sort-Object FullName|ForEach-Object {$h[[IO.Path]::GetRelativePath($dir,$_.FullName)]=(Get-FileHash -LiteralPath $_.FullName).Hash};return ($h|ConvertTo-Json -Compress)}
function Run($name,[string[]]$arguments,$expected=0){& dotnet $cli @arguments *> (Join-Path $Output ($name+'.log'));$code=$LASTEXITCODE;$script:runs+=@{name=$name;exitCode=$code;expected=$expected};if($code -ne $expected){throw "$name exit $code, expected $expected"}}
$before=Hashes $Bundle;$visualBefore=Hashes $Fields
Run base @('--render-map',$Bundle,(Join-Path $Output 'Base'))
Run enhanced @('--render-map',$Bundle,(Join-Path $Output 'Enhanced'),'--visual',$Fields)
Run repeat @('--render-map',$Bundle,(Join-Path $Output 'Repeat'),'--visual',$Fields)
if((Hashes (Join-Path $Output 'Enhanced')) -cne (Hashes (Join-Path $Output 'Repeat'))){throw 'Repeated render files differ.'}
$published=Get-Content (Join-Path $Output 'Enhanced/render-manifest.json') -Raw|ConvertFrom-Json
foreach($entry in $published.outputs.PSObject.Properties){
 if($entry.Name -notin @('map.png','style-used.json','visual-style-used.json','legend.json','legend.md') -or (Get-FileHash -LiteralPath (Join-Path $Output ('Enhanced/'+$entry.Name))).Hash -cne $entry.Value){throw 'Published visual output hash mismatch.'}
}
Run decoded-water @('--check-cartographic-output',$Bundle,(Join-Path $Output 'Enhanced'),(Join-Path $Output 'Base'))
foreach($mode in @('disabled','forest-only','ground-only')){
 $style=[ordered]@{format='sp-visual-style/1';forest=($mode -eq 'forest-only');ground=($mode -eq 'ground-only')}
 $file=Join-Path $Output ($mode+'.json');$style|ConvertTo-Json|Set-Content $file -Encoding utf8
 Run $mode @('--render-map',$Bundle,(Join-Path $Output $mode),'--visual',$Fields,'--visual-style',$file)
}
$baseHash=(Get-FileHash (Join-Path $Output 'Base/map.png')).Hash
if($baseHash -cne (Get-FileHash (Join-Path $Output 'disabled/map.png')).Hash){throw 'Disabled controls do not reproduce base map.'}
foreach($mode in @('forest-only','ground-only')){if($baseHash -ceq (Get-FileHash (Join-Path $Output "$mode/map.png")).Hash){throw "No visible effect: $mode"}}
Run wrong-world @('--render-map',$OtherBundle,(Join-Path $Output 'Rejected-world'),'--visual',$Fields) 1
foreach($case in @('wrong-parent','wrong-orientation','wrong-sampling','bad-hash','truncated','nan','unknown-class','invalid-class-table')){
 $dir=Join-Path $Output ('fixture-'+$case);$null=New-Item -ItemType Directory -Path $dir
 foreach($file in @('visual-manifest.json','forest.f32','ground.rgba.f32','variants.i32')){Copy-Item -LiteralPath (Join-Path $Fields $file) -Destination $dir}
 $path=Join-Path $dir 'visual-manifest.json';$m=Get-Content $path -Raw|ConvertFrom-Json
 switch($case){
  'wrong-parent' {$m.parentManifestSha256='0'*64}
  'wrong-orientation' {$m.orientation='south-first'}
  'wrong-sampling' {$m.sampling='unknown'}
  'bad-hash' {$m.files.'forest.f32'='0'*64}
  'invalid-class-table' {$m.classes=@(@('not-empty'))}
  {$_ -in 'truncated','nan','unknown-class'} {
   $file=if($case -eq 'unknown-class'){'variants.i32'}else{'forest.f32'};$data=Join-Path $dir $file
   $stream=[IO.File]::OpenWrite($data)
   try {if($case -eq 'truncated'){$stream.SetLength(8)}else{$bytes=if($case -eq 'nan'){[BitConverter]::GetBytes([single]::NaN)}else{[BitConverter]::GetBytes([int]999999)};$stream.Write($bytes,0,4)}} finally {$stream.Dispose()}
   $m.files.$file=(Get-FileHash $data).Hash
  }
 }
 $m|ConvertTo-Json -Depth 20|Set-Content $path -Encoding utf8
 $dest=Join-Path $Output ('Rejected-'+$case);Run $case @('--render-map',$Bundle,$dest,'--visual',$dir) 1
 if(Test-Path -LiteralPath $dest){throw 'Invalid fields published a render.'}
}
foreach($entry in @(@('unknown-style','{"format":"sp-visual-style/1","typo":1}'),@('bad-style','{"format":"sp-visual-style/1","forestStrength":2}'),@('duplicate-style','{"format":"sp-visual-style/1","forest":true,"forest":false}'))){
 $file=Join-Path $Output ($entry[0]+'.json');[IO.File]::WriteAllText($file,$entry[1]);Run $entry[0] @('--render-map',$Bundle,(Join-Path $Output ('Rejected-'+$entry[0])),'--visual',$Fields,'--visual-style',$file) 1
}
if($before -cne (Hashes $Bundle) -or $visualBefore -cne (Hashes $Fields)){throw 'Source files changed.'}
[ordered]@{status='passed';runs=$runs;allSourceFilesUnchanged=$true;allRepeatedOutputHashesExact=$true;disabledControlsExactBasePng=$true}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $Output 'validation.json') -Encoding utf8
Write-Output "Visual rendering verified: $($runs.Count) processes."
