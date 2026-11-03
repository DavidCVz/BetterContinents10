param(
    [Parameter(Mandatory)][string]$Left,
    [Parameter(Mandatory)][string]$Right,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Output) { throw 'Output must be a new file.' }
$a = Get-Content -LiteralPath $Left -Raw | ConvertFrom-Json
$b = Get-Content -LiteralPath $Right -Raw | ConvertFrom-Json
if ($a.format -ne 'sp-precision/1' -or $b.format -ne $a.format) { throw 'Unsupported trace format.' }
if ($a.samples.Count -ne $b.samples.Count -or $a.settingsVersion -ne $b.settingsVersion) { throw 'Trace shape/settings mismatch.' }
for ($i = 0; $i -lt $a.samples.Count; $i++) {
    if ($a.samples[$i].x.bits -ne $b.samples[$i].x.bits -or $a.samples[$i].z.bits -ne $b.samples[$i].z.bits -or $a.samples[$i].layers.Count -ne $b.samples[$i].layers.Count) {
        throw "Coordinate/layer mismatch at sample $i."
    }
}
function Collect-Bits($node, [string]$path, $result) {
    if ($null -eq $node) { return }
    if ($node -is [pscustomobject]) {
        if ($node.PSObject.Properties['bits']) { $result[$path] = $node.bits; return }
        foreach ($property in $node.PSObject.Properties) { Collect-Bits $property.Value "$path.$($property.Name)" $result }
    } elseif ($node -is [System.Collections.IList]) {
        for ($j = 0; $j -lt $node.Count; $j++) { Collect-Bits $node[$j] "$path[$j]" $result }
    }
}
$leftBits = @{}; $rightBits = @{}
Collect-Bits $a 'trace' $leftBits; Collect-Bits $b 'trace' $rightBits
$differences = @(); $missing = @(); $equal = 0
foreach ($path in @(@($leftBits.Keys) + @($rightBits.Keys) | Sort-Object -Unique)) {
    if (!$leftBits.ContainsKey($path) -or !$rightBits.ContainsKey($path)) { $missing += $path; continue }
    if ($leftBits[$path] -ne $rightBits[$path]) {
        $differences += [pscustomobject]@{Path=$path; LeftBits=$leftBits[$path]; RightBits=$rightBits[$path]}
    } else { $equal++ }
}
$report = [pscustomobject]@{
    Format='sp-precision-comparison/1'; SampleCount=$a.samples.Count
    LeftSha256=(Get-FileHash -LiteralPath $Left).Hash; RightSha256=(Get-FileHash -LiteralPath $Right).Hash
    EqualObservedFloats=$equal; DifferentObservedFloats=$differences.Count
    Differences=$differences; MissingObservationPaths=$missing
    Limitation='Diagnostic bit comparison only; matching observer values do not establish full generation parity. Match capture manifests separately.'
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Output
Write-Output "Precision trace: $($a.samples.Count) samples; $equal equal, $($differences.Count) different float observations; $($missing.Count) absent observations."
