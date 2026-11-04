# Standalone Mono V3 compatibility probe

## F04 comparison and instrumentation

The full twelve-input reference matrix now passes: 787,740 points, 100% biome
agreement, maximum surface error 0.034149169921875m and mean error
0.0001283873637910109m. All 785,208 points outside the +/-0.10m ocean band have
identical classification; 2,532 are inside, with 19 differences retained. The
surface threshold applies to every point. These results cover the tested build,
four presets, three seeds and static unedited non-LOD mesh/ocean contract.
The formal 36-process benchmark is tracked separately from capture durations.

F04 is complete for that scope. The benchmark completed three sequential fresh
1024² workers per input (36 runs): generation + PNG took 39.460–47.272 seconds,
with per-input medians 39.500–47.182 seconds, preflight measured separately.
Maximum observed OS peak working sets were 326.38MiB for the worker and
98.33MiB for the separate renderer, sampled every 100ms and at exit when
available. All 216 numeric/PNG files match the F03 baselines. Case-sensitive
grouping preserves AbC123 versus abc123. Full hardware/conditions and per-phase
measurements stay in local evidence. These timings describe this prototype on
the measured system, not a latency target or all hardware.

Recommendation: proceed to a local application around the isolated worker with
progress/cancellation and result caching, then evaluate low-resolution preview
and refinement. Runtime/game dependency preparation and extracted catalog inputs
still need a product setup workflow. A web version requires separate evaluation;
no application UI or deployment is part of this completed technical milestone.

The first actual global reference (0.2.0, Continents/AbC123) passed all 65,645
biomes with max/mean/P95 surface errors 0.031829833984375 /
0.00011615274765289186 / 0.0003566741943359375 metres. Static ocean classification
has zero differences outside the band; 215 points are inside, with one difference.
Independent comparisons have identical TSV hashes. This validates one input,
not the twelve-combination matrix or CA-08.

The comparator accepts additive 0.2.1 manifests with selective/full-native mode
and requires 700 in-game native checks for selective mode. The shared
SelectiveTerrainBuilder used only for capture is linked into the worker to
test its reverse-patched IL loop bounds: 300 node checks match native full
builder bits in three pilot zones. No noise, blend or mesh interpolation code
is shared between this reference helper and SparseSurface. F02's regression
also passes. The actual 0.2.1 capture completed in 31,060ms with 700 native checks;
all four captured data files match the full reference hashes, and the comparison
TSV is identical. The selective capture route is accepted for the remaining
eleven inputs, each still requiring its own native preflight and reference.

`Compare-GlobalReference.ps1` takes `-Mono`, `-Bootstrap`, the known 0.1.5
`-PilotReference`, new 0.2.0 global `-Reference`, `-Preset`, `-Seed`, `-Output`.
It rejects incompatible schema/builds/preset/seed/version/hashes, then runs the
known pilot gate before reconstructing the requested world. GlobalComparison
checks effective settings, ocean plane and every regular-grid coordinate/anchor,
uniqueness, finiteness and group counts. It evaluates exact reference positions
with SparseSurface, independently of the reference's full native mesh builder.
It reports count/max/mean/P95, biome differences and water counts/differences
inside AND outside +/-0.10m, overall and by group. Surface tolerance is 0.10m
everywhere; biome differences and water differences outside the band fail.
Only a passed combination receives a final manifest. Twelve actual references
are required for F04 acceptance; malformed synthetic input tests prove rejection
only, and a real global reference has not yet been compared.

`Measure-TerrainMaps.ps1 -Mono ... -Bootstrap ... -PilotReference ... -Output ...`
runs 36 sequential fresh 1024² workers after a separate pilot preflight. It
records configuration, world initialization/native audit, sampling, binary I/O,
PNG export and process wall times, plus OS peak working set queried every 100ms
and at exit when accessible. Worker/renderer memory peaks are separate. CPU,
RAM, OS, runtime/worker/renderer hashes and cache conditions stay in local output;
no user/device names or serial numbers are requested. The full run requires
CPU/RAM read access and a closed game. `-SmokeTest` runs only one 64² process,
explicitly labeled as insufficient for CA-08; partial hardware discovery is
allowed only in that mode. A complete run reports median/range per combination.

Sampling now buffers the three layers before binary I/O so their durations
are measured separately (9 bytes/pixel additional buffers). Numeric files and
PNG bytes must remain identical to the F03 baselines. Global fidelity and the
36-run performance campaign are pending the first directed global game test.

## Experimental F03 terrain pilot

### Complete offline maps

Build both the Mono worker and ../seed-previewer image renderer first, with
the original dependencies prepared. Run with the same verified x64 runtime,
terrain bootstrap and 0.1.5 reference used for the pilot:

```powershell
./Run-TerrainMap.ps1 -Mono path/to/mono-x64/bin/mono.exe -Bootstrap .local/new-terrain -Reference path/to/capture-0.1.5 -Preset Continents -Seed AbC123 -Resolution 1024 -Output .local/new-map
./Verify-TerrainMaps.ps1 -Mono path/to/mono-x64/bin/mono.exe -Bootstrap .local/new-terrain -Reference path/to/capture-0.1.5 -Output .local/new-matrix
```

Run-TerrainMap first runs the complete matching pilot gate, then generates a
fresh world for the requested seed/preset using only the captured authored
catalog, never captured assignments or heights. SparseSurface evaluates the
needed 1m vertices using real GetBiomeHeight and DUtils helpers, preserving the
original builder's corner-biome blend. It checks bitwise equality against real
HeightmapBuilder meshes before every map, including modified/distant sectors.
The original assembly_valheim is unchanged. Cache lifetime is bounded by zone
rows. No game process, cache wrapper, world save or builder thread is needed.

The map directory contains surface.f32 metres, biomes.i32 flags, below-ocean.u8,
three PNG previews, palette/encoding metadata, effective settings and sector
summaries. All are north-first pixel centers across BC.TotalSize. Surface PNG
is autoscaled L16 for inspection; the f32 file preserves metres. Biome PNG uses
documented categorical colors (unknown flags magenta); water PNG is white below
the static 30m plane. Lakes are explicitly unsupported. The final manifest is
published only after native checks, finite output and rendering pass, with
hashes of every map file and input provenance through the pilot gate.

Verify-TerrainMaps generates four presets x three seeds x two processes at 1024²,
two isolated child jobs at a time, and compares numeric layers, PNGs, settings
and sector summaries. Numbered seed slots preserve AbC123/abc123 on Windows.
It requires twelve distinct surface maps. Timings are recorded separately for
initialization and sampling; timing fields are not determinism targets. This
is offline repeatability, not full-map validation against the game or F04.

The verified campaign completed all 24 runs: twelve distinct maps, identical
pairs, and 1,926,600 native-builder vertex checks with zero bit differences.
All 360 files listed in the map manifests passed final hash validation. This
fulfills F03/CA-04 for the tested build and presets, while global fidelity,
coastal/transition reference coverage and formal performance remain in F04.

`MeshSurface` samples the unedited non-LOD 1m mesh triangles: SW/NW/SE and
SE/NW/NE. Heightmap.GetWorldHeight selects a vertex, and continuous biome-height
evaluation/bilinear interpolation are different contracts. A non-coplanar cell
check covers both triangles, their diagonal, the outer node and invalid input.
Each run exports three local 128x128 patches at 0.5m pixel centers, north first:
surface.f32 metres, biomes.i32 flags, below-ocean.u8 (1 below 30m), little-endian.
Layer metadata and hashes are in layers.json and mesh-layers.json. Lakes are
rejected. These are candidate local raster layers, not twelve full-world maps.

Capture 0.1.5 adds actual triangle indices and 61,440 fractional Unity plane
samples. Verify-Native checks their hashes; MeshSurface verifies every index,
compares biome/surface and static ocean classification (excluding +/-0.10m
around the reference plane). The surface threshold remains 0.10m. Comparison
must pass before the runner publishes its final success manifest. A 0.1.4 run
remains supported and explicitly labels its mesh layers as awaiting a fractional
reference. The directed 0.1.5 test passed 61,440 samples: maximum surface error
0.00070953369140625m, zero biome/water differences; topology exact. Repeated
outputs match. Altered topology or surface with recomputed hashes is rejected.

Capture 0.1.4 supplies the authored alt-biome catalog and independent live sector
reference. BiomeContext imports only authored scalar/enum/string values, registers
the mod's sector patches, and runs GenerateBiomePoints/GenerateSectors including
modifier placement. It avoids the cache/save wrapper. All 858 sector summaries,
including 37 modified sectors, and 12,675 pilot sector lookups match exactly.
Reference heights or sector assignments are never installed in the generator.
`terrain-context.json` records readiness, and `sector-comparison.json` records
the comparison. This replaces the earlier prototype's empty-sector fallback.
TerrainRow in WorldExport provides a continuous non-LOD blending model. The
surface contract above evaluates actual 1m vertices and then mesh triangles.

The terrain detours require **x64 Mono**. The Unity Editor's bundled mono.exe
tested for F02 is x86; instance methods returning double fail with Harmony under
that runtime. F02's arithmetic-only route remains unchanged and validated.

The tested alternative is the [official Mono 6.12.0.206 x64 package](https://www.mono-project.com/download/stable/).
Its MSI was verified with a valid Microsoft signature and extracted into ignored
staging without executing installer actions or installing globally. Runtime
payloads are not included in Git. A custom embedding host attempted during
diagnosis was blocked by Windows security and abandoned; no exclusion or bypass
was used. The official executable passes both HarmonyX and Lib.Harmony minimal
double-return patch/unpatch checks.

```powershell
./Prepare-Terrain.ps1 -BaseBootstrap .local/prepared -Output .local/new-terrain
./Run-TerrainPilot.ps1 -Mono path/to/mono-x64/bin/mono.exe -Bootstrap .local/new-terrain -Reference path/to/capture-0.1.4 -Output .local/new-terrain-run
```

The runner scopes MONO_PATH to that runtime's class libraries and uses
`--optimize=-float32`. This flag is necessary for base-noise parity; the default
x64 arithmetic retains the earlier 8,067 base-height differences. Explicit
float32 boundaries in the primitive candidates remain necessary as well.

The terrain bootstrap changes copied Perlin/RNG bindings to NativeBridge and
sets the standalone Debug.isDebugBuild flag to false. The latter controls
ZLog.DevLog diagnostics in modifier placement; it supplies no terrain values.
It uses original game formulas and the real WorldGeneratorPatch/DynamicPatch paths,
and invokes actual HeightmapBuilder.Build synchronously with its real request
constructor. It does not start the builder thread or reproduce its blend formula.
Two runs match all 12,675 pilot biomes and surface values exactly. Complete-map
exports and their repetition are handled separately above; global game parity
remains pending. Logs still contain unresolved Quaternion
binding warnings while vegetation patch methods are prepared; these are not
silenced with synthetic rotations and must be addressed if a required path uses them.
Native failures in experimental modes retain logs/exit codes without Windows
crash dialogs. Original DLLs and the game profile are unchanged.

F03 investigation: `UnityIntegerRandomCandidate.cs` and `UnityPerlinCandidate.cs`
are wired only into the explicit copied terrain bootstrap, through NativeBridge.
Capture 0.1.3 validates 625 Perlin outputs, 1,024 RNG outputs and 4,128 state words
exactly under the tested Mono. Explicit float32 storage is necessary; the naive
algebraically equivalent expressions differ. `NativeFloat` uses a stack union
and no-inlining to preserve that boundary without allocating per operation.
This establishes parity only over the captured primitive domain, not all inputs
or terrain. The Perlin candidate is an inferred absolute-coordinate 3D slice
with explicit normalization; it is not a published Unity native implementation.

`Verify-ConstructorRandom.ps1 -Reference <pilot> -Output <new-local-json>` repeats
the original 21-call check. `Verify-Native.ps1 -Mono <mono> -Reference <capture-0.1.3>
-Output <new-local-directory>` validates hashes/build/float encodings and creates
fixtures, then executes candidate comparison under Mono. See `comparison.json`.
Algorithm investigation: [original RNG experiment](https://gist.github.com/macklinb/a00be6b616cbf20fa95e4227575fe50b).

Mono worker for the integrated F02 entry point `../seed-previewer/Run-Preview.ps1`.
This invokes the actual Better Continents V3
base-height prefix, including edge and center branches, under an installed Mono
runtime. It does not construct a native WorldGenerator or produce biomes, final
surface or water classification. The entry point uses the .NET image exporter
to add PNG previews; the original .NET height generator remains a diagnostic.

Prepare the original dependencies and mod build using `../seed-previewer/` first.
Use the same workspace-local CLI/NuGet caches as that tool when building.

```powershell
./Prepare-Bootstrap.ps1 -Output .local/bootstrap -Mono $Mono
dotnet build MonoV3Probe.csproj -c Release
& $Mono bin/Release/net48/MonoV3Probe.exe ../.. .local/bootstrap Continents AbC123 .local/example
# Optional sixth argument: directory containing the matching captured points.tsv.
```

`$Mono` is a user-supplied path to the installed Mono executable. No runtime,
game DLL or adapted DLL is distributed here. The tested runtime comes from the
locally installed Unity Editor; the editor and game do not run during the probe.
Run from this directory. Every output directory must be new. Seeds are restricted
to 1–10 ASCII letters/digits and the four shipped presets are supported.

## Explicit bootstrap adaptations

The preparation script uses the local Mono.Cecil dependency to create two copies:

- BetterContinents.dll: insert `conv.r4` immediately after the raw noise return in
  `WarpedNoise.GetNoise`, preserving the float32 boundary validated against the pilot.
- assembly_utils.dll: replace only the `Application.persistentDataPath` getter in
  the Utils initializer with an empty string. All remaining initializer work and
  terrain utility methods are retained. The arithmetic helpers used by V3 do not
  read this path. Save APIs are outside the supported probe and are never called.

The script rejects unexpected call shapes and records original/adapted hashes
plus an IL audit. These files are local diagnostics. Never install these adapted
assemblies in Valheim, Thunderstore, or the original dependency directories.

## Output and evidence boundaries

The worker writes a finite float32 grid (1024² by default) with north-first pixel centers,
`generation.json`, settings lifecycle/dump, the 18-node precision trace with the real V3 callback, and
`branches.json`. Optional pilot mode samples exact TSV coordinates and writes
`pilot.tsv`. Exit 0 means the requested execution succeeded (and, in pilot mode,
all parsed base heights matched); 2 means pilot discrepancies; 1 means failure.

Direct worker mode alone does not validate reference manifests or provenance.
Use `Run-Preview.ps1` to validate bootstrap/runtime, capture hashes, identity,
coordinates and effective settings, render the PNG and publish `manifest.json`
only after completion. A sixth argument of `-` skips pilot mode; an optional
seventh argument specifies resolution 64..2048. The supported captured pilot is
Continents/AbC123. Full terrain fidelity remains pending in later phases.

Branch checks temporarily adjust in-memory flags and sea offset to exercise the
real center/edge code, then restore settings in finally. They assert boundary
behavior without replacing the formula. These are behavioral checks, not new
in-game reference measurements for those branches.

Generated evidence may contain local paths. Keep it under `.local/` or outside
the repository and review it before sharing. The bootstrap directory is required
at runtime; the original game files remain unchanged.
