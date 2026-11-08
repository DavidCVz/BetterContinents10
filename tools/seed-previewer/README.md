# Seed Previewer — offline probes and cartographic renderer

## Measured visual controls (milestone 2 / F03)

```powershell
dotnet bin/Release/net10.0/SeedPreviewer.dll --render-map <M1-bundle> <new-output> --visual <visual-fields>
# Optional independent recipes: --style <base-style.json> --visual-style <visual-style.json>
```

`sp-visual-fields/1` binds additional layers to the exact parent manifest and four
source hashes. The renderer rejects another world/resolution/coordinate contract,
missing provenance, damaged/truncated layers, non-finite values and unknown
variant classes. Source files stay unchanged. `forest.f32` is the real patched
GetForestFactor at each M1 pixel center (Y=0), not normalized tree density.
`ground.rgba.f32` preserves all four raw procedural GetBiomeHeight mask channels
at the same point and its categorical biome. Negative values remain in the raw
data. `variants.i32` indexes ordered variant-name combinations queried through
GetBiomeSector; categories are never interpolated.

The point procedural ground control is distinct from the blended 1m terrain
paint texture after quantization, player edits and GPU filtering. This map uses
the former as a cartographic signal, not a rendered terrain-texture replica or a
lava hazard map. The native reference separately checks direct masks and four
native-builder mask nodes, without using the candidate's mask-blending formula.

The 32 authored definitions in the validated catalog all have texture override
None. The domain therefore has no alternate texture replacement to render;
variant classes are retained as data. Nonzero overrides in another catalog are
explicitly unsupported, not silently approximated or presented as tested.

`sp-visual-style/1` has `forest`/`ground` toggles (default true), strengths
forestStrength=0.35, mossStrength=0.65 and lavaStrength=0.9 (each 0-1), and opaque
colors forestColor=#263E2C, mossColor=#71834A, lavaColor=#E76A25. Unknown/duplicate
properties and invalid values fail; format is required in custom files.
`visual-style-used.json` and all source hashes are recorded with the render.

The continuous color transfer is our own: forest weight is clamped
(1.15-factor)/0.6 in Meadows, (0.8-factor)/0.5 in Plains and (1.6-factor) in
BlackForest. Other biomes receive no forest tint. Mistlands uses clamped mask
alpha as ground cover; Ashlands uses clamped (alpha-0.5)*2 for warm material.
These are documented artistic thresholds, not inferred tree positions, normalized
source values or the game's collision/hazard thresholds. Water pixels receive
no visual-control overlay. Disabling both channels reproduces the base PNG exactly:

```json
{"format":"sp-visual-style/1","forest":false,"ground":false}
```

`Verify-VisualRendering.ps1 -Bundle <Continents-bundle> -Fields <fields>
-OtherBundle <Swirls-bundle> -Output <new-evidence>` checks separate-process
repetition, decoded pixels, all ocean pixels, channel toggles, source preservation
and invalid fields/styles. The existing F02 cartographic campaign remains valid.
See the Mono worker README for native comparison and field export admission.

## Reproducible cartography (milestone 2 / F02)

From this tool directory, after building:

```powershell
dotnet bin/Release/net10.0/SeedPreviewer.dll --render-map <M1-bundle-root> <new-output>
dotnet bin/Release/net10.0/SeedPreviewer.dll --render-map <M1-bundle-root> <another-new-output> --style <style.json>
```

This path reads existing numerical layers only. It does not initialize a world,
invoke Mono, open Valheim or require another capture. The input is a complete
`Run-TerrainMap.ps1` bundle with `manifest.json` and its `map/` directory, not a PNG
or a bare map directory. Supported contracts are the four M1 presets, 1-10 ASCII
alphanumeric seed characters, 64-2048 pixels, north-first pixel centers, metre
heights and the static 30 m ocean plane. Layer hashes, exact dimensions, finite
heights, known biome IDs, water values and height/mask agreement are checked.
Missing, damaged or inconsistent inputs fail with exit 1.

The destination must be new and outside the input bundle. Successful output:

- `map.png`: RGBA8 composite with metric relief, depth, shoreline and land-biome fade.
- `style-used.json`: full effective recipe, including defaults.
- `legend.md` / `legend.json`: palette, units, depth ramp and interpretation limits.
- `render-manifest.json`: completion marker, input manifest/layer hashes, effective
  style hash, renderer and image-library hashes, .NET runtime, PNG/metadata hashes
  and decoded row-major RGBA hash. No absolute input/output paths are recorded.

Files are prepared in a uniquely named sibling `.partial-*` directory and the
complete directory is renamed into place. An existing destination is never
replaced. A failure during export leaves an unpublished partial directory with a
failure marker; use a new destination for a retry. Invalid input/style is rejected
before creating the render directory. Original data and diagnostic PNGs are kept.

Styles use format `sp-cartographic-style/1`. Omitted properties use fixed defaults;
unknown/duplicate properties, invalid types/ranges and non-`#RRGGBB` colors fail.
Copy an exported `style-used.json` to adjust the complete recipe, or use a partial
style such as:

```json
{"format":"sp-cartographic-style/1","fadeMetres":120,"exaggeration":1.5,"transparent":true}
```

The default is 120 m nominal land-biome fade, 3x visual relief, NW light (azimuth
315 degrees in X/Z, altitude 45), ambient 0.48/diffuse 0.74, circular crop and
opaque `#101923` background. `exaggeration` changes illumination only. Set
`fadeMetres: 0` for hard category colors; `circular: false` exports the full square.
`transparent: true` uses circular alpha instead of the background. Depth colors
interpolate from `shallow` to `shelf` over 35 m, then to `deep` over another 180 m;
these distances are fixed style values, not per-map normalization.

Fade diffuses RGB and the snow-tint weight on the four-neighbor land graph. A
water edge is never crossed, even between nearby disconnected islands. No biome
ID, height or mask is interpolated or changed. The width means the nominal
10%-90% transition across a straight boundary: sigma = width / 2.563103, in
metres. Raster spacing and land topology affect the measured width; narrow
connected land regions can mix more than one neighboring color. The explicit
diffusion step is at most 0.2 and the work limit is 128 steps. Excessive width at
the requested spacing is rejected, never silently clamped.

Numeric limits: fade 0-500 m (subject to that work limit), exaggeration 0-8,
azimuth 0-360, altitude 1-90, ambient/diffuse/shoreTint 0-1, shelfDepth 0.1-1000 m,
deepRange 0.1-2000 m and shoreHeight 0.1-100 m. If `palette` is supplied, include
exactly IDs 1, 2, 4, 8, 16, 32, 64, 256 and 512; names/colors are in the legend.
The other color fields are shallow, shelf, deep, sand, snow and background.

Without `--visual`, this is **base terrain**. Measured controls can be supplied
through milestone 2 / F03 above. Blended colors are presentation, not a
categorical biome query or tree/object map. No decorative terrain noise is added.

Verification (output must be new):

```powershell
dotnet bin/Release/net10.0/SeedPreviewer.dll --check-cartographic-renderer
./Verify-Cartography.ps1 -Bundle <Continents-bundle> -SecondBundle <Swirls-bundle> -Output <new-evidence>
# Optional: -SampleBaseline <F01-Continents-sample-directory>
```

The campaign uses the M1 1024px / 21000m fixtures. The script runs separate
processes, compares every repeated output hash, verifies
decoded pixels/manifests, checks all real water pixels against a zero-fade render,
tests invalid inputs/styles and overwrite protection, and checks all original
bundle files before/after. An isolated runtime copy with only the CLI, its .NET
metadata and ImageSharp produces identical outputs without game/mod/Harmony DLLs.
The geometric fixtures check light axes, metric fade
at two spacings, land connectivity, snow-weight continuity and alpha; they are
not evidence about world generation. Diagnostic M1 and optional F01 PNGs are
also compared by hash. Determinism is verified in the tested runtime/encoder
environment, not promised across arbitrary library or runtime versions.

The historical .NET base-height probe is documented below. The supported full
terrain route from milestone 1 is `../seed-previewer-mono/Run-TerrainMap.ps1`;
rendering its existing bundles is independent of that worker.

## Cartographic visual sample (milestone 2 / F01)

`dotnet bin/Release/net10.0/SeedPreviewer.dll --render-map-sample <M1-bundle-root> <new-output>`
reads the existing bundle manifest and map layers, verifies source hashes, and
produces map.png, map-soft.png, map-transparent.png and four 256px crops with
matching categorical-biome crops. sample.json records source hashes, geometry,
palette, lighting, crop coordinates and limitations. No world generation or game
process is invoked. `--check-cartography` checks metric gradients and light axes.

This visual direction was approved for F01; the general renderer and land-biome
color transitions are now available through `--render-map` above. The historical
sample keeps its original hard boundaries. It uses a fixed palette, metric hillshade (NW light), static
ocean depth, a height-based shore tint and a circular display crop derived from
the recorded span. Sources stay unchanged. Green/dark terrain is biome coloring,
not extracted forest density; lava/moss masks and alt-biome visual overrides
are pending separate work. The two relief variants use 3x/1.5x visual vertical
exaggeration only. Raw heights are never exaggerated or regenerated.

This console can read real presets (F01) or sample real V3 base height (F02).
It does not start Valheim, save a world, or modify player saves/configuration.
It is not yet a biome or final-surface previewer.

The F03 terrain route now lives in `../seed-previewer-mono/Run-TerrainMap.ps1`.
This executable also supplies `--render-terrain <map-directory>` for its PNG
exports. See that tool's README for runtime/bootstrap/reference requirements,
the sparse triangular surface contract and offline matrix verification.

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
in base height. A local, restricted Mono noise audit initially reduced it; adding
an explicit float32 conversion after the raw noise call in a disposable DLL copy
then eliminated it at all 12,675 pilot nodes, with an identical separate-process
repeat. That change does not eliminate the .NET 10 residual. This is a diagnostic
Mono experiment, not a production V3 implementation or an adopted runtime change.
Exact base-height equivalence of the .NET prototype is therefore still pending; this does
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
was exercised in-game, including the precision observations added in version
0.1.2. The new pilot passed integrity checks and reproduced the earlier terrain
samples. F03 remains partial: native adaptation and full parity are pending.

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

## Full V3 Mono compatibility experiment

The separate `../seed-previewer-mono/` probe now invokes the real V3 prefix under
Mono, using explicit local bootstrap copies. Its complete pilot matches 12,675
base heights and all 890 trace observations, including actual final callbacks.
Twenty-four processes generated twelve distinct 1024² grids with identical hashes
for each repeated seed/preset pair. Behavioral edge/center checks also passed.

This does not change this .NET CLI's runtime or results. The Mono worker is integrated with provenance controls through the entry point below; it does not resolve
biomes, final surface, water, or full-world game fidelity. See its README for the
two exact bootstrap adaptations and reproduction instructions.

## Integrated F02 entry point (recommended for base height)

`Run-Preview.ps1` runs the Mono worker and the .NET image exporter as separate
processes. Build both projects first, using the local caches described above.
Prepare a **new** bootstrap directory with the tested Mono executable:

```powershell
../seed-previewer-mono/Prepare-Bootstrap.ps1 -Output ../seed-previewer-mono/.local/prepared -Mono $Mono
./Run-Preview.ps1 -Mono $Mono -Bootstrap ../seed-previewer-mono/.local/prepared -Preset Continents -Seed AbC123 -Resolution 1024 -Output .local/preview
# Optional: -Reference path/to/validated-Continents-AbC123-capture
./Verify-Mono.ps1 -Mono $Mono -Bootstrap ../seed-previewer-mono/.local/prepared -Reference path/to/capture -Evidence .local/new-matrix
```

The entry point rejects stale original/adapted assemblies or changed runtime
identity. Reference mode validates capture hashes, game/build, plugins, unique
zone coordinates, numeric seed and exact effective settings. It checks sampled
float bits again with the .NET parser and reports maximum/mean error per zone.
The only captured pilot currently supported is Continents/AbC123.

Outputs include `height-base.f32`, a 16-bit grayscale `height-base.png`, settings
dump/package/lifecycle, the actual V3 precision trace, and a completion
`manifest.json` containing runtime/build/preset hashes and coordinate semantics.
`generation.json` alone is intermediate worker evidence. Failed runs may leave
partial outputs but do not publish a completion manifest for incompatible input.
A valid reference with numerical discrepancies is explicitly marked and reported
as an error, without applying tolerance. F02 acceptance covers base height and
repeatability; this does not validate biomes, final surface, water or world-wide
fidelity. Generated outputs must stay local and be reviewed before sharing.
