# Seed Previewer — F02 real base-height prototype

This console can read real presets (F01) or sample real V3 base height (F02).
It does not start Valheim, save a world, or modify player saves/configuration.
It is not yet a biome or final-surface previewer.

## Requirements

- Windows, .NET SDK/runtime 10 and .NET Framework 4.8 reference assemblies.
- Local Valheim installation and BepInEx core files from a local mod-manager profile.
- NuGet access on the first build. Dependencies are cached under ignored `.local/`.

Validated in F01 with Valheim 1.0.21 (Steam build 26249040), Unity
6000.0.75.2503836, Better Continents 0.10.7, BepInEx 5.4.23.6, SDK 10.0.401 and
runtime 10.0.12. Game/Unity versions came from the existing BepInEx log; assembly
hashes in the local phase evidence identify the files actually used. Compatibility
of world generation is still unverified.

## Prepare and build

From this directory in PowerShell, substitute your installation/profile paths:

```powershell
./Prepare.ps1 -GameDirectory 'path/to/Valheim' -BepInExCore 'path/to/mod-profile/BepInEx/core'
```

The script preflights inputs, copies managed references into `.local/libs/`, then
builds the unchanged target framework of the mod (net4.8) and this probe (net10.0).
It changes `DOTNET_CLI_HOME` and `NUGET_PACKAGES` for the current PowerShell session
to workspace-local directories. It does not install anything into the game.

The main project's optional `LibsTools` MSBuild property overrides its reference
directory; without an override it preserves upstream's `../libs-Tools/` default.
Valheim 1.0.21's `com.rlabrecque.steamworks.net.dll` gets a local filename alias
`steamworks.net.dll`; its contents are not patched. The probe resolves dependencies
from its own output first, then the supplied reference and mod dependency directories.
It uses Lib.Harmony 2.3.6 instead of BepInEx's HarmonyX, following upstream's offline
tools: HarmonyX's initializer failed under this runtime during F01.

Use one game/BepInEx version per staging directory. When changing versions, use a
fresh checkout or clear only this probe's local build outputs and `.local` staging
before preparing again, to avoid retaining references from an older installation.

## Read a real preset

With Valheim closed:

```powershell
dotnet bin/Release/net10.0/SeedPreviewer.dll .local/libs ../../obj/Release/net4.8/BetterContinents.dll ../../bin/Release/net4.8 ../../assets/BCAssets/Presets/Continents.BetterContinents
```

Success returns exit code 0 and one JSON object on stdout; mod diagnostics go to
stderr. Repeat with Pangaea, Swirls and Vanillaish. A missing dependency, missing
preset or deserialization failure returns exit code 1 with an explicit error.
The pre-ILRepack mod assembly in `obj` is deliberate; ImageSharp and other mod
dependencies resolve from `bin`. Do not replace this with the merged distribution DLL.

The F01 low-level read preserves the preset's saved version (the four shipped files
are version 7). It does **not** apply new-world config defaults or upgrade it to
version 12. The F02 command below applies the actual new-world builder. The game's
assembly version is 0.0.0.0 and cannot identify its release.

## Generate base height (F02)

After preparing/building, from this directory:

```powershell
dotnet bin/Release/net10.0/SeedPreviewer.dll .local/libs ../../obj/Release/net4.8/BetterContinents.dll ../../bin/Release/net4.8 ../../assets/BCAssets/Presets/Continents.BetterContinents --height AbC123 1024 .local/height-example
```

The output directory must not exist. Supported inputs for this milestone are the
four exact shipped preset fixtures, seeds of 1–10 ASCII letters/digits, and sizes
64–2048. This is a deliberately restricted seed subset, not a complete replica of
the game's UI input validator. Case matters. Unsupported inputs fail explicitly.

The command invokes `NewWorldBuild.Build` with declared in-memory mod defaults,
the preset path and the default Wide Sectors setting; it does not read the player's
config. New-world alt-biome defaults are applied by the actual preset loader.
The presets retain settings version 7. Geometry uses the mod's `UpdateGeometry`.

Seed conversion uses the installed game's managed `GetStableHashCode` method,
identified from the World constructor's IL, and cross-checks a separate stable-hash
implementation. The actual `InitializePrefix` seeds the mod's `NoiseStack`.
`GetBaseHeightPrefixV3` performs every grid sample. No synthetic terrain or noise
replacement is used. The game constructor initializes mountain distance to 1000 m;
the actual `VersionSetup(2)` method is applied to obtain the effective restriction.

The full WorldGenerator constructor is intentionally not executed: its native
Unity random offsets are unused by this V3 base-height path. Their faithful
initialization remains unresolved for F03, not emulated by arbitrary values.

Outputs:

- `height-base.f32`: row-major little-endian float32 in normalized game height units
  (multiply by 200 for metres on the game's height scale), not final terrain height.
- `height-base.png`: 16-bit grayscale normalized between the recorded min/max;
  display only, without biome colors or a final coastline claim.
- `manifest.json`: seed, hashes, versions, coordinates, encoding, range and preliminary metrics.
- `effective-settings.txt` and `default-config.json`: actual settings and default configuration.

Row 0 points north (+Z); columns increase east (+X). The manifest defines exact
pixel-centre coordinates. Failed runs may leave partial files, but do not produce
a success manifest. Use a fresh output directory for a retry.

Run the reproducibility campaign (24 separate processes) with Valheim closed:

```powershell
./Verify-F02.ps1 -EvidenceDirectory .local/verification-example
```

It covers three fixed seeds across four presets, repeats each generation, checks
binary sizes/hashes and distinct results, verifies invalid inputs and overwrite
protection, and reruns the F01 reader. Output names have seed indices because
Windows paths cannot distinguish two seeds by letter case alone.

The optional `--inspect-game` command after the four paths prints relevant local
game IL for development audit. Keep that output local rather than distributing
game code. The F02 phase report records the audit and its boundaries.
Diagnostic logs and exception stack traces may include local filesystem paths.
Keep generated evidence in `.local/` and review it before sharing it externally.

## Scope and known limitations

No synthetic terrain, native-noise emulation or generation patches are installed.
Preset loading and V3 base-height repeatability are proven. Native random offsets,
biome delegation, final surface and fidelity against in-game samples are not yet
validated. A repeatable image alone does not demonstrate agreement with Valheim.

The upstream ImageSharp 2.1.13 dependency emits NuGet vulnerability warnings. F01
preserves that dependency for compatibility and only reads the repository's trusted
presets. Evaluate remediation before accepting arbitrary image inputs or publishing.

Existing reused mod code remains under the repository's LGPL-2.1 license; FastNoiseLite
has its own license under `FastNoiseLite/LICENSE`. External DLLs remain local ignored
inputs. No game/BepInEx DLLs are included as source or redistribution artifacts.
