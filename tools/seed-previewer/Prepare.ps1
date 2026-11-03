param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [Parameter(Mandatory)][string]$BepInExCore
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$managed = Join-Path $GameDirectory 'valheim_Data/Managed'
$libs = Join-Path $PSScriptRoot '.local/libs'
# Preflight before copying; all writes stay in the ignored local staging directory.
foreach ($name in @('assembly_valheim', 'assembly_utils', 'assembly_guiutils', 'netstandard', 'UnityEngine',
    'UnityEngine.CoreModule', 'UnityEngine.InputLegacyModule', 'UnityEngine.UI', 'UnityEngine.UIModule',
    'UnityEngine.AssetBundleModule', 'UnityEngine.ImageConversionModule', 'UnityEngine.TextRenderingModule',
    'UnityEngine.IMGUIModule', 'UnityEngine.PhysicsModule', 'UnityEngine.AudioModule', 'Unity.TextMeshPro',
    'gui_framework', 'SoftReferenceableAssets', 'Splatform')) {
    if (!(Test-Path -LiteralPath "$managed/$name.dll" -PathType Leaf)) { throw "Missing dependency: $managed/$name.dll" }
}
if (!(Test-Path "$managed/steamworks.net.dll") -and !(Test-Path "$managed/com.rlabrecque.steamworks.net.dll")) {
    throw "Missing Steamworks managed assembly in $managed"
}
foreach ($file in @("$BepInExCore/BepInEx.dll", "$BepInExCore/0Harmony.dll")) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing dependency: $file" }
}
New-Item -ItemType Directory -Force $libs | Out-Null
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | Copy-Item -Destination $libs
Get-ChildItem -LiteralPath $BepInExCore -Filter '*.dll' | Copy-Item -Destination $libs
# Valheim 1.0.21 uses this filename; preserve the upstream project's expected alias.
if (!(Test-Path "$managed/steamworks.net.dll") -and (Test-Path "$managed/com.rlabrecque.steamworks.net.dll")) {
    Copy-Item "$libs/com.rlabrecque.steamworks.net.dll" "$libs/steamworks.net.dll"
}
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.local/dotnet'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.local/packages'
& dotnet build "$repo/BetterContinents.csproj" -c Release "-p:LibsTools=$libs/" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed; see compiler output.' }
& dotnet build "$PSScriptRoot/SeedPreviewer.csproj" -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed; see compiler output.' }
