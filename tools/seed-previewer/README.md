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
The preset objects start at settings version 7. The sampler now also calls the real
disk serializer (`Serialize` with `network=false`) and loader before sampling, matching
the world's save/load cycle: these fixtures load as version 11. Geometry then uses
the mod's `UpdateGeometry`. The codec omits default alt-biome options when
`IsLegacyEquivalent` is true at the world's radius, so the loaded fixtures use the
legacy settings. No player save is read or changed by this round trip.

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
- `before-save-settings.txt`, `saved-settings.bin` and `settings-lifecycle.json`:
  pre-save dump, real package bytes and before/after scalar float bits.

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

## Compare a captured base-height pilot

```powershell
dotnet bin/Release/net10.0/SeedPreviewer.dll .local/libs ../../obj/Release/net4.8/BetterContinents.dll ../../bin/Release/net4.8 ../../assets/BCAssets/Presets/Continents.BetterContinents --compare-base AbC123 path/to/captured-pilot .local/base-comparison-example
```

This samples the exact TSV coordinates, not the raster pixels or PNG, and writes
per-point errors/bits plus a summary by zone. It rejects mismatched seeds, game or
distribution DLL hashes, plugin inventory and corrupted points/settings. The
effective dump is compared at Mono's seven-significant-digit display precision
because runtime-default text formatting differs; this is supplemental evidence,
not proof that every in-memory field has identical bits. The lifecycle trace
separately preserves scalar float bits before/after serialization.

Exit 0 means exact float-bit equality at every sampled pilot node. Exit 2 means
numerical discrepancies remain and require analysis; no tolerance is silently
applied. Exit 1 is an invalid input or incompatible/corrupt reference. The captured
distribution DLL and offline pre-ILRepack DLL are different artifacts from the same
build; both identities must be preserved, not compared as identical byte streams.

Current measured .NET 10 residual against the real pilot is at most about 0.113 mm
in base height. A local, restricted Mono noise audit reduced it but did not fully
eliminate it. Exact base-height equivalence is therefore still pending; this does
not establish biome, final-surface or water fidelity. The 24-run matrix remains
deterministic after applying the real settings round trip.

## Scope and known limitations

### F03 native dependency diagnostic

After building, append `--diagnose-terrain` to the four input paths of the read-only
probe. It actually invokes the installed Unity Perlin/RNG bindings in an isolated
process and emits a JSON diagnostic plus a direct-call inventory of relevant game
methods. Exit 2 means at least one binding is unavailable; no substitute noise or
biome map is emitted. On the validated .NET 10 environment, all four probes fail
with `SecurityException: ECall methods must be packaged into a system module.`

The in-game capture tool is in `../seed-previewer-reference/`. Its initial pilot
was exercised in-game; version 0.1.2 adds precision observations that still need a
new real capture. F03 remains partial: native adaptation and full parity are pending.

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

## Intermediate float diagnostics

`--compare-base` writes `precision-trace.json` using `PrecisionTrace.cs`, the same
observer linked into reference plugin 0.1.2. It records 18 fixed coordinates with
scalar settings, normalization ranges, warped coordinates, raw/filtered noise,
layer outputs and final base height as float values plus authoritative hex bits.
The usual `--height` command does not run this additional observer.

```powershell
./Compare-Precision.ps1 -Left path/to/offline/precision-trace.json -Right path/to/game/precision-trace.json -Output .local/precision-comparison.json
```

The comparator rejects mismatched formats, coordinate order, settings versions and
layer counts; it reports missing observations separately. Match the enclosing
capture manifests and hashes before using this diagnostic. It does not grant
generation acceptance or apply a numerical tolerance.
