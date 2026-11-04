param(
    [Parameter(Mandatory)][string]$Bundle,
    [Parameter(Mandatory)][string]$SecondBundle,
    [Parameter(Mandatory)][string]$Output,
    [string]$SampleBaseline,
    [string]$Cli = (Join-Path $PSScriptRoot 'bin/Release/net10.0/SeedPreviewer.dll')
)
$ErrorActionPreference = 'Stop'
$Bundle = (Resolve-Path -LiteralPath $Bundle).Path
$SecondBundle = (Resolve-Path -LiteralPath $SecondBundle).Path
$Output = [IO.Path]::GetFullPath($Output)
foreach ($source in @($Bundle, $SecondBundle)) {
    $geometry = Get-Content -LiteralPath (Join-Path $source 'map/map.json') -Raw | ConvertFrom-Json
    if ($geometry.size -ne 1024 -or $geometry.totalSizeMetres -ne 21000) { throw 'This verification campaign requires the M1 1024px / 21000m fixtures.' }
}
if (Test-Path -LiteralPath $Output) { throw 'Evidence directory must be new.' }
foreach ($source in @($Bundle, $SecondBundle)) {
    if ($Output.StartsWith($source.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence must be outside source bundles.' }
}
$null = New-Item -ItemType Directory -Path $Output
$runs = [Collections.Generic.List[object]]::new()
function Hashes([string]$Directory) {
    $result = [ordered]@{}
    Get-ChildItem -LiteralPath $Directory -Recurse -File | Sort-Object FullName | ForEach-Object {
        $result[[IO.Path]::GetRelativePath($Directory, $_.FullName)] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    return $result
}
function Same($A, $B) { return (($A | ConvertTo-Json -Depth 20 -Compress) -ceq ($B | ConvertTo-Json -Depth 20 -Compress)) }
function Run([string]$Name, [string[]]$Arguments, [int]$Expected = 0) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $text = & dotnet $Cli @Arguments 2>&1
    $code = $LASTEXITCODE
    $text | Out-File -LiteralPath (Join-Path $Output ($Name + '.log')) -Encoding utf8
    $runs.Add([ordered]@{ name=$Name; exitCode=$code; expected=$Expected; elapsedMs=$watch.ElapsedMilliseconds })
    if ($code -ne $Expected) { throw "$Name returned $code, expected $Expected." }
}
function Fixture([string]$Name) {
    $dir = Join-Path $Output ('fixtures/' + $Name)
    $null = New-Item -ItemType Directory -Path (Join-Path $dir 'map')
    Copy-Item -LiteralPath (Join-Path $Bundle 'manifest.json') -Destination $dir
    foreach ($file in @('surface.f32','biomes.i32','below-ocean.u8','map.json')) {
        Copy-Item -LiteralPath (Join-Path $Bundle ('map/' + $file)) -Destination (Join-Path $dir 'map')
    }
    return $dir
}
function Rehash([string]$Dir, [string]$File) {
    $path = Join-Path $Dir 'manifest.json'
    $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    ($manifest.Files | Where-Object File -CEQ $File).Sha256 = (Get-FileHash -LiteralPath (Join-Path $Dir ('map/' + $File))).Hash
    $manifest | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $path -Encoding utf8
}
function Reject([string]$Name, [string]$Source, [string]$Style) {
    $dest = Join-Path $Output ('rejected-' + $Name)
    $arguments = @('--render-map', $Source, $dest)
    if ($Style) { $arguments += @('--style', $Style) }
    Run $Name $arguments 1
    if (Test-Path -LiteralPath $dest) { throw "$Name published an invalid output." }
}
$before = Hashes $Bundle
$secondBefore = Hashes $SecondBundle
Run 'math' @('--check-cartographic-renderer')
Run 'default' @('--render-map', $Bundle, (Join-Path $Output 'Default'))
Run 'repeat' @('--render-map', $Bundle, (Join-Path $Output 'Repeat'))
if (!(Same (Hashes (Join-Path $Output 'Default')) (Hashes (Join-Path $Output 'Repeat')))) { throw 'Separate-process repeat differs.' }
$runtime = Join-Path $Output 'DataOnlyRuntime'
$null = New-Item -ItemType Directory -Path $runtime
foreach ($file in @('SeedPreviewer.dll','SeedPreviewer.deps.json','SeedPreviewer.runtimeconfig.json','SixLabors.ImageSharp.dll')) {
    Copy-Item -LiteralPath (Join-Path (Split-Path $Cli) $file) -Destination $runtime
}
$normalCli = $Cli
try {
    $Cli = Join-Path $runtime 'SeedPreviewer.dll'
    Run 'data-only-runtime' @('--render-map', $Bundle, (Join-Path $Output 'DataOnlyRender'))
} finally { $Cli = $normalCli }
if (!(Same (Hashes (Join-Path $Output 'Default')) (Hashes (Join-Path $Output 'DataOnlyRender')))) { throw 'Render without game/mod/Harmony DLLs differs.' }
$style = Get-Content -LiteralPath (Join-Path $Output 'Default/style-used.json') -Raw | ConvertFrom-Json
$style.fadeMetres = 0
$zeroStyle = Join-Path $Output 'no-fade.json'
$style | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $zeroStyle -Encoding utf8
Run 'no-fade' @('--render-map', $Bundle, (Join-Path $Output 'NoFade'), '--style', $zeroStyle)
Run 'published-output' @('--check-cartographic-output', $Bundle, (Join-Path $Output 'Default'), (Join-Path $Output 'NoFade'))
$style.fadeMetres = 120; $style.transparent = $true; $style.exaggeration = 1.5
$variant = Join-Path $Output 'transparent-soft.json'
$style | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $variant -Encoding utf8
Run 'variant' @('--render-map', $Bundle, (Join-Path $Output 'TransparentSoft'), '--style', $variant)
Run 'second-world' @('--render-map', $SecondBundle, (Join-Path $Output 'SecondWorld'))
$default = Get-Content -LiteralPath (Join-Path $Output 'Default/render-manifest.json') -Raw | ConvertFrom-Json
$noFade = Get-Content -LiteralPath (Join-Path $Output 'NoFade/render-manifest.json') -Raw | ConvertFrom-Json
if ($default.pixelSha256 -ceq $noFade.pixelSha256) { throw 'Fade did not change image.' }
if (!(Same $default.sourceLayers $noFade.sourceLayers)) { throw 'Style changed source identity.' }
$existingBefore = Hashes (Join-Path $Output 'Default')
Run 'existing-output' @('--render-map', $Bundle, (Join-Path $Output 'Default')) 1
if (!(Same $existingBefore (Hashes (Join-Path $Output 'Default')))) { throw 'Existing output was changed.' }
Run 'nested-output' @('--render-map', $Bundle, (Join-Path $Bundle 'rejected-render')) 1
Run 'invalid-cli' @('--render-map', $Bundle, (Join-Path $Output 'bad-cli'), '--unknown', $variant) 1

foreach ($kind in @('truncated','wrong-hash','nan','infinity','biome','water','mask-disagrees','dimensions','orientation','units','identity','incomplete','missing')) {
    $dir = Fixture $kind
    switch ($kind) {
        'truncated' { $stream = [IO.File]::OpenWrite((Join-Path $dir 'map/surface.f32')); try { $stream.SetLength(100) } finally { $stream.Dispose() }; Rehash $dir 'surface.f32' }
        'wrong-hash' { [IO.File]::WriteAllBytes((Join-Path $dir 'map/below-ocean.u8'), [byte[]]@(1)) }
        {$_ -in 'nan','infinity','biome','water','mask-disagrees'} {
            $file = if ($kind -in 'nan','infinity') {'surface.f32'} elseif ($kind -eq 'biome') {'biomes.i32'} else {'below-ocean.u8'}
            $path = Join-Path $dir ('map/' + $file)
            $stream = [IO.File]::OpenWrite($path)
            try {
                $bytes = switch ($kind) {
                    'nan' { [BitConverter]::GetBytes([single]::NaN) }
                    'infinity' { [BitConverter]::GetBytes([single]::PositiveInfinity) }
                    'biome' { [BitConverter]::GetBytes([int]999) }
                    'water' { [byte[]]@(2) }
                    'mask-disagrees' { $original = [IO.File]::ReadAllBytes((Join-Path $Bundle 'map/below-ocean.u8'))[0]; [byte[]]@([byte](1-$original)) }
                }
                $stream.Write([byte[]]$bytes, 0, $bytes.Length)
            } finally { $stream.Dispose() }
            Rehash $dir $file
        }
        {$_ -in 'dimensions','orientation','units'} {
            $path = Join-Path $dir 'map/map.json'; $info = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            if ($kind -eq 'dimensions') { $info.size = 512 }
            if ($kind -eq 'orientation') { $info.orientation = 'south-first' }
            if ($kind -eq 'units') { $info.surface = 'normalized height' }
            $info | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $path -Encoding utf8; Rehash $dir 'map.json'
        }
        {$_ -in 'identity','incomplete'} {
            $path = Join-Path $dir 'manifest.json'; $info = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            if ($kind -eq 'identity') { $info.Seed = 'Mismatch' } else { $info.Status = 'partial' }
            $info | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $path -Encoding utf8
        }
        'missing' { Remove-Item -LiteralPath (Join-Path $dir 'map/biomes.i32') }
    }
    Reject $kind $dir
}
$invalidStyles = [ordered]@{
    'style-format'='{"format":"unknown"}'
    'style-unknown'='{"format":"sp-cartographic-style/1","typo":true}'
    'style-range'='{"format":"sp-cartographic-style/1","fadeMetres":-1}'
    'style-color'='{"format":"sp-cartographic-style/1","deep":"red"}'
    'style-palette'='{"format":"sp-cartographic-style/1","palette":{"999":"#000000"}}'
    'style-null'='{"format":"sp-cartographic-style/1","palette":null}'
    'style-duplicate'='{"format":"sp-cartographic-style/1","fadeMetres":0,"fadeMetres":120}'
    'style-work-limit'='{"format":"sp-cartographic-style/1","fadeMetres":500}'
}
foreach ($entry in $invalidStyles.GetEnumerator()) {
    $path = Join-Path $Output ($entry.Key + '.json'); [IO.File]::WriteAllText($path, $entry.Value); Reject $entry.Key $Bundle $path
}
$diagnostic = Fixture 'diagnostic-regression'
Run 'diagnostic-regression' @('--render-terrain', (Join-Path $diagnostic 'map'))
foreach ($file in @('biomes.png','surface.png','below-ocean.png')) {
    if ((Get-FileHash -LiteralPath (Join-Path $diagnostic ('map/' + $file))).Hash -cne (Get-FileHash -LiteralPath (Join-Path $Bundle ('map/' + $file))).Hash) { throw 'Diagnostic PNG regression.' }
}
if ($SampleBaseline) {
    Run 'sample-regression' @('--render-map-sample', $Bundle, (Join-Path $Output 'SampleRegression'))
    foreach ($file in (Get-ChildItem -LiteralPath $SampleBaseline -Filter '*.png')) {
        if ((Get-FileHash -LiteralPath (Join-Path $Output ('SampleRegression/' + $file.Name))).Hash -cne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw 'F01 sample PNG regression.' }
    }
}
if (!(Same $before (Hashes $Bundle)) -or !(Same $secondBefore (Hashes $SecondBundle))) { throw 'Original bundle changed.' }
[ordered]@{ status='passed'; runs=$runs; allOriginalBundleFilesUnchanged=$true; separateProcessAllOutputHashesExact=$true; runtimeWithoutGameOrModDllsExact=$true; defaultPixelSha256=$default.pixelSha256; sourceHashes=$before } | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $Output 'Validation.json') -Encoding utf8
Write-Host "Cartography verification passed ($($runs.Count) processes)."
