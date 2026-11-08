param(
    [Parameter(Mandatory)][string]$Settings,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][ValidateSet('Continents','Pangaea','Swirls','Vanillaish')][string]$Preset,
    [Parameter(Mandatory)][ValidatePattern('\A[A-Za-z0-9]{1,10}\z')][string]$Seed,
    [Parameter(Mandatory)][ValidateSet(512,1024)][int]$Resolution
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$workspace = Get-Content -LiteralPath $Settings -Raw | ConvertFrom-Json
if ($workspace.format -cne 'seed-previewer-workspace/1') {
    throw 'Configuración del entorno incompatible.'
}

function Resolve-Dependency([string]$Name) {
    $value = $workspace.$Name
    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
        throw "Falta configurar $Name."
    }
    $path = [IO.Path]::GetFullPath($value, $repo)
    if (-not (Test-Path -LiteralPath $path)) {
        throw "La dependencia $Name no está disponible. Revisa la configuración del entorno."
    }
    return $path
}

Write-Output 'STAGE|Comprobando el entorno'
$mono = Resolve-Dependency 'mono'
$bootstrap = Resolve-Dependency 'bootstrap'
$pilot = Resolve-Dependency 'pilot'
$reference = Resolve-Dependency 'visualReference'
$comparison = Resolve-Dependency 'visualComparison'
$worker = Join-Path $repo 'tools/seed-previewer-mono'
$renderer = Join-Path $repo 'tools/seed-previewer/bin/Release/net10.0/SeedPreviewer.dll'
if (-not (Test-Path -LiteralPath $renderer)) {
    throw 'Compila el renderizador SeedPreviewer en modo Release.'
}

$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'La carpeta de salida ya existe.' }
New-Item -ItemType Directory -Path $Output | Out-Null
$bundle = Join-Path $Output 'terrain'
$fields = Join-Path $Output 'visual'
$render = Join-Path $Output 'render'

Write-Output 'STAGE|Generando terreno y biomas'
& (Join-Path $worker 'Run-TerrainMap.ps1') -Mono $mono -Bootstrap $bootstrap -Reference $pilot `
    -Preset $Preset -Seed $Seed -Resolution $Resolution -Output $bundle *> (Join-Path $Output 'terrain.log')

Write-Output 'STAGE|Preparando bosque y suelo'
& (Join-Path $worker 'Export-VisualFields.ps1') -Mono $mono -Bootstrap $bootstrap -Reference $reference `
    -Comparison $comparison -Bundle $bundle -Output $fields *> (Join-Path $Output 'visual.log')

Write-Output 'STAGE|Dibujando A Actual'
& dotnet $renderer --render-map $bundle $render --visual $fields *> (Join-Path $Output 'render.log')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $render 'render-manifest.json'))) {
    throw 'Falló el dibujo del mapa. Consulta render.log.'
}
