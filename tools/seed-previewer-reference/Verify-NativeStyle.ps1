param([Parameter(Mandatory)][string]$Reference,[Parameter(Mandatory)][string]$Repo,[Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$ExpectedProbeSha256,[Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$ExpectedModSha256,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$Reference=(Resolve-Path -LiteralPath $Reference).Path;$Repo=(Resolve-Path -LiteralPath $Repo).Path;$Output=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $Output){throw 'Verification output must be new.'}
$m=Get-Content -LiteralPath (Join-Path $Reference 'manifest.json') -Raw|ConvertFrom-Json -Depth 40
if($m.format -cne 'sp-native-map-style/1' -or $m.status -cne 'complete-palette-basic-inventory' -or $m.seed -cne 'AbC123' -or $m.paletteBiomes -ne 9 -or $m.stablePaletteReads -ne 2 -or $m.texturePixelsExported -ne $false){throw 'Native style identity/contract mismatch.'}
if($m.probeSha256 -ine $ExpectedProbeSha256 -or $m.modSha256 -ine $ExpectedModSha256){throw 'Native style probe/mod identity mismatch.'}
foreach($pair in @(@('assembly_valheim.dll','gameSha256'),@('UnityEngine.CoreModule.dll','coreSha256'))){if((Get-FileHash -LiteralPath (Join-Path $Repo ('tools/seed-previewer/.local/libs/'+$pair[0]))).Hash -ine $m.($pair[1])){throw 'Native style game/core build mismatch.'}}
if((@($m.plugins.guid|Sort-Object)-join ',') -cne 'BetterContinents,local.seedpreviewer.reference' -or @($m.plugins|Where-Object {$_.guid -ceq 'local.seedpreviewer.reference' -and $_.version -ceq '0.4.0'}).Count -ne 1){throw 'Unexpected style capture plugins.'}
foreach($pair in @(@('palette.json','paletteSha256'),@('material-inventory.json','inventorySha256'),@('effective-settings.txt','settingsSha256'))){if((Get-FileHash -LiteralPath (Join-Path $Reference $pair[0])).Hash -ine $m.($pair[1])){throw 'Native style file integrity failed.'}}
$palette=Get-Content -LiteralPath (Join-Path $Reference 'palette.json') -Raw|ConvertFrom-Json -Depth 60
if($palette.format -cne 'sp-native-map-palette/1' -or $palette.colorSpace -notin @('Linear','Gamma')){throw 'Palette format/color-space unknown.'}
$fields=@{1='m_meadowsColor';2='m_swampColor';4='m_mountainColor';8='m_blackforestColor';16='m_heathColor';32='m_ashlandsColor';64='m_deepnorthColor';256=$null;512='m_mistlandsColor'}
function Decode($raw){
 if($raw.bits -cnotmatch '^[0-9A-F]{8}$'){throw 'Invalid float bits.'}
 $value=[BitConverter]::ToSingle([BitConverter]::GetBytes([Convert]::ToUInt32($raw.bits,16)),0)
 if([single]::IsNaN($value) -or [single]::IsInfinity($value) -or $value -ne [single]$raw.value){throw 'Invalid float value.'}
 return $value
}
$fingerprints=@();$values=0
foreach($read in @($palette.first,$palette.second)){
 if(@($read.rows).Count -ne 9 -or (@($read.rows.biome|Sort-Object -Unique).Count -ne 9)){throw 'Palette biome inventory incomplete.'}
 $fingerprint=@()
 foreach($row in $read.rows){
  $id=[int]$row.biome;if(!$fields.ContainsKey($id) -or $row.field -cne $fields[$id]){throw 'Native palette field mapping mismatch.'}
  foreach($color in @($row.expected,$row.native)){
   if(@($color.raw).Count -ne 4 -or @($color.rgba8).Count -ne 4){throw 'RGBA channels incomplete.'}
   $bytes=@();for($i=0;$i -lt 4;$i++){
    $f=Decode $color.raw[$i];$product=[single]([Math]::Clamp([double]$f,0,1)*255.0);$byte=[int][Math]::Round([double]$product,[MidpointRounding]::ToEven)
    if($byte -ne $color.rgba8[$i]){throw 'Native Color32 conversion differs.'};$bytes+=$byte;$values++
   }
   if(($bytes|ForEach-Object {$_.ToString('X2')}) -join '' -cne $color.hexRgba){throw 'RGBA8 hex differs.'}
  }
  for($i=0;$i -lt 4;$i++){
   if($row.native.raw[$i].bits -cne $row.expected.raw[$i].bits){throw 'Native GetPixelColor differs from expected field.'}
   if($id -eq 256 -and $row.native.raw[$i].bits -cne '3F800000'){throw 'Unexpected Ocean fallback.'}
  }
  $fingerprint+=@{biome=$id;bits=(@($row.native.raw.bits)-join ',')}
 }
 $fingerprints+=,($fingerprint|Sort-Object biome|ConvertTo-Json -Compress)
 foreach($color in @($read.forestColor,$read.noForestColor)){if(@($color.raw).Count -ne 4){throw 'Forest mask color channels missing.'};foreach($channel in $color.raw){$null=Decode $channel}}
}
if($fingerprints[0] -cne $fingerprints[1] -or $palette.second.frame -lt $palette.first.frame+2){throw 'Repeated palette/frame check failed.'}
$inventory=Get-Content -LiteralPath (Join-Path $Reference 'material-inventory.json') -Raw|ConvertFrom-Json -Depth 80
if(@($inventory.fieldMaterials).Count -ne 2 -or @($inventory.ui).Count -ne 2 -or @($inventory.generatedInputs).Count -ne 4){throw 'Basic inventory structure incomplete.'}
$null=New-Item -ItemType Directory -Path $Output
[ordered]@{status='palette-verified-not-final-shader-colors';biomes=9;stableReads=2;rgbaChannelConversionsChecked=$values;colorSpace=$palette.colorSpace;referenceManifestSha256=(Get-FileHash -LiteralPath (Join-Path $Reference 'manifest.json')).Hash;paletteSha256=$m.paletteSha256;texturePixelsExported=$false;materialInventoryScope='basic references/properties, availability must be reviewed separately'}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $Output 'validation.json') -Encoding utf8
Write-Output 'Native base palette checks passed; material availability and final colors remain separate.'
