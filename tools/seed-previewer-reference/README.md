# In-game reference pilot — development only

## F04 global reference (plugin 0.2.1)

Update **0.2.1** defaults to selective native capture. A Harmony reverse patch
copies the installed HeightmapBuilder.Build body into a private stand-in and
changes only the two row/column loop starts and ends, computing a 2x2 node cell.
Width, scale, corner queries, formulas, float operations and masks remain native.
The engine's Build entry point is not patched. Exactly four expected IL edits
are required; a different loop shape fails. It does not use the offline sampler.
Before capturing, seven full native zones verify 700 node values bitwise against
the selective copy. Stand-in bounds are thread-local, and the cell cache is
bounded. Capture yields on an 8ms time budget or 64 grid samples, supports cancel,
and records mode, check count and elapsed wall time in the manifest.

The first 0.2.0 full capture passed 65,645 points for Continents/AbC123, taking
approximately 29.34 wall-clock minutes (file timestamps, potentially including
pauses). Max surface error 0.031829833984375m, zero biome differences and zero
water differences outside the 0.10m band; one difference inside the band among
215 points is retained. Global acceptance for the other eleven worlds is pending.

The real 0.2.1 test completed in **31,060ms** with 700 native checks. All four
data files (65,645 points, settings, biome context and topology) match the full
0.2.0 reference hashes exactly; the offline comparison TSV is identical too.
The optimization is accepted for continuing the matrix, with native preflight
checks required for every new world. Other worlds still need their own references.
The old full route is retained
as `sp_reference_global Continents full` for independent controls when needed.

Create a disposable world for the requested preset/seed; do not change settings
after loading. First directed test: SPF04C1, seed AbC123, preset Continents.
Once its terrain has loaded, run `sp_reference_global Continents` in F5.
The default uses the selective original builder body, never the sparse offline
evaluator. It prints completed rows, yields within a frame budget, and can be cancelled with
`sp_reference_cancel`. Keep the world/settings loaded until completion.

Success: `Global reference captured (256x256 + 109 extra points).`
Output is under BepInEx/seed-previewer-global-reference in a fresh directory.
No complete manifest is written on cancellation/failure. No worlds are created
or saved by this command. It supports the three planned seeds and four preset
labels; the label is checked against actual effective settings offline.

The capture adds 65,536 north-first pixel centers spanning BC.TotalSize, 50
coastal and 50 biome-transition points, and nine center/boundary anchors. Extra
points are chosen from reference grid neighbor differences, distributed across
the candidate list, and refined by twelve native bisection steps. No offline
point set or interpolation formula is shared. Triangle planes, authored catalog,
world generation version, seed, effective settings, plugins and file hashes are
recorded. The static ocean plane is checked at 30m; Lakes/other geometry fail.
Reference cache is bounded to 32 zone requests; engine-ready requests are consumed.
RNG is restored around synchronous builder requests, never across a frame yield.
The twelve planned inputs have now passed the independent offline comparison:
787,740 points, exact biomes and zero ocean-classification differences outside
the band. See the Mono tool README for metrics and the scope of this validation.

Extension **0.1.5** passed the directed game test. The same command
adds `mesh-triangles.i32` (actual loaded non-LOD collision mesh indices) and
`fractional-points.tsv` (61,440 points in the three existing zones). Heights use
Unity Plane.Raycast on triangles with original builder heights and captured
topology; the offline sampler is not shared. This is a triangle-plane reference,
not a PhysX/scene raycast. No temporary colliders, world edits or scene objects
are created. Water is classification against the static ocean plane only.
The loaded player heightmap must have 65x65 vertices; otherwise capture fails.
Success prints `Reference pilot captured (fractional mesh included).`
Both new files have hashes in the manifest. All 61,440 biome and static-ocean
classifications match the offline triangle sampler. Maximum surface difference
is 0.00070953369140625m (mean 0.000106117739947346m), within the existing 0.10m
threshold. Actual indices match in every cell. These are three-zone geometric
samples, not complete-world parity, PhysX or arbitrary water-volume coverage.

Previous extension **0.1.4** passed its directed game test. It adds
`biome-context.json` and its hash: authored scalar/enum/string alt-biome catalog,
the 12,675 pilot sectors and their modifier names, readiness of the world's
biome-sector data, and the ocean/prefab water planes with the lakes flag.
Spawn/vegetation/location asset objects, player identifiers and save paths are
excluded. The captured world has 32 catalog entries, 858 sectors and 37 modified
sectors. Offline reconstruction now matches all captured sector summaries and
pilot lookups; complete-world surface and water fidelity remain pending.
Use the existing disposable world and command, without changing its settings.
Success prints `Reference pilot captured (biome context included).`

Previous capture version: **0.1.3**, exercised in the directed game test.
It adds `native-primitives.json` and `nativeSha256` to the existing pilot:
625 Perlin samples (signed coordinates, lattice boundaries and fractions), eight
seeded RNG sequences with initial/step state bits, integer/float/equal/reversed
ranges and `Random.value`, plus the live ZoneSystem ocean water baseline.
The RNG state is restored in finally without yielding. This does not capture
per-point water-volume coverage or establish land/water fidelity.

The 0.1.3 build used the same disposable world and `sp_reference_pilot`, printing
`Reference pilot captured (precision and native probes included).`
The 0.1.2 results below remain historical evidence. The 0.1.3 capture passed hash
and build checks and preserves the same prior points/settings/precision hashes.
Its native samples support the new candidate checks, not full terrain acceptance.

Status: reference plugin 0.1.2 was exercised in-game, including its shared
float-bit diagnostics. The new capture passed integrity and build-identity checks;
its 12,675 terrain samples match the earlier pilots exactly. This is a validation
tool, not an offline generator or a solution to the native binding gate.

Build after `../seed-previewer/Prepare.ps1` has prepared the dependencies:

```powershell
dotnet build SeedPreviewerReference.csproj -c Release
```

Use the same workspace-local NuGet/CLI cache as the preparation script if needed.
The only output to deploy for this tool is `bin/Release/net48/SeedPreviewerReference.dll`.
It relies on the game's BepInEx and Better Continents; do not copy reference/game DLLs.

## Native map palette / basic inventory (0.4.0, Plan 03 F02)

Load the existing disposable `SPF04C1` world (AbC123) through Develop. Open the
large map with M, then open F5 and run `sp_reference_style`. Keeping the large
map visible helps observe its already-bound UI material; the capture does not
force an update or change UI assignments. Wait for
`Native map style captured: 9 biome colors, two stable reads.` before leaving.
This capture reads state across two frames and does not generate terrain.

Output: `BepInEx/seed-previewer-style-reference/<id>/`. palette.json records
active fields, native GetPixelColor results, raw float bits, Unity Color32
conversion and repeated observations. Conversion to bytes performs no explicit
gamma transform; it is not a claim about the final shader color. The original
constructor defaults are not substituted for active serialized field values.

material-inventory.json records bounded basic references and properties from the
two material fields, UI configured/first bound material when available, and the
four generated input textures. Inventory entries may explicitly be unavailable
or partial. No texture pixels, shader source or original asset bundles are
exported. This is preparation for later resource analysis, not full extraction.

manifest.json is published only after palette reads agree bit-for-bit and match
the native method/field associations. Errors print a failure message and details
go to the BepInEx log. Existing pilot/global/visual commands remain available.
Only one capture may run at a time. The command neither assigns materials nor
modifies generation, world saves or existing bundles. Native object IDs in the
inventory identify objects only within that capture, not stable asset identities.

`Verify-NativeStyle.ps1` checks source/probe/mod identities, hashes, native field
associations, exact repeats, raw floats and the Color32 byte conversion. Supply
`-Reference`, `-Repo`, expected probe/mod SHA256 values and a new `-Output`.
It verifies a base palette; material coverage and final shader output require
separate review. Synthetic validator fixtures are not in-game reference evidence.

## Milestone 2 visual reference (0.3.0)

With the Develop test profile, load the existing disposable `SPF04C1` world
(Continents / `AbC123`) and run `sp_reference_visual` once in F5. Do not change
world settings or switch worlds during capture. No travel or new world is needed.
Wait for `Visual reference captured: N points.` before leaving the world.
This capture is separate from `sp_reference_global`; do not rerun the 12 worlds.

The capture selects real forest/material ranges from a 64x64 discovery scan and
adds modified-sector centers. It stores float values/bits for forest factor,
direct RGBA ground mask, four native 1m builder mask nodes around each explicit
point, ordered variants/texture overrides and the native sector texture code.
That code contains shader weights, not display RGB. A selective native builder
copy is checked against 100 full-builder nodes for all RGBA bits and heights;
the candidate mask formula is not used to produce reference observations.

Output is under `BepInEx/seed-previewer-visual-reference/<unique-id>/` with
settings, authored catalog/sector context, visual points and a success manifest.
There is no successful manifest after failure. No automatic capture, world edits,
teleports or generation patches are installed. Capture may take time; progress
continues across frames. Errors are available in the BepInEx log. The world must
actually be the stated preset; offline comparison checks settings/builds before
acceptance. The preset label alone is not proof.

Offline comparison: `../seed-previewer-mono/Compare-VisualReference.ps1` accepts
the tested Mono/bootstrap, existing M1 fractional `-Pilot`, new `-Reference` and
new `-Output`. It checks provenance and the M1 pilot before comparing raw values.
This is a point-level gate; full raster fields, 12-world regression and artistic
mapping remain pending until those observations pass.

## Pilot procedure

1. Use a disposable test profile and a separate save directory with Valheim 1.0.21,
   BepInEx and the Better Continents build from the same source used by the offline
   prototype. No other mods that change world generation. Do not use a personal world.
2. Add this probe DLL to that test profile's `BepInEx/plugins/`. Enable the Valheim
   console with the usual `-console` launch option. No captures run automatically.
3. Create/load a disposable world with seed `AbC123`, preset Continents, and the mod's
   default config. Wait until generation has completed.
4. Run `sp_reference_pilot` in the game's console. The probe requests real generated
   terrain for three 64 m zones and writes 12,675 sample rows, plus a manifest and
   effective settings under `BepInEx/seed-previewer-reference/<unique-id>/`.
   Version 0.1.2 also writes `precision-trace.json` and its hash in the manifest.
   It prints `Reference pilot captured (precision trace included).` to the F5
   console after success, in addition to the BepInEx log.
5. A completed `manifest.json` marks a successful capture. Validate coordinates and
   heights against known terrain, match settings and seed to the offline manifest,
   and inspect all errors before using the data as a reference. No success manifest
   is written after a failure. A valid capture does not establish offline parity.
6. Remove the probe DLL after capture. The plugin does not patch generation, create
   worlds, place objects, modify terrain or explicitly save worlds. Normal game
   activity still saves the disposable test world.

## Data and boundaries

- Biomes use the loaded, patched game generator.
- Surface samples come from `HeightmapBuilder.RequestTerrainSync`, the game's
  terrain builder; no offline blend formula is used to establish the reference.
- Coordinates are explicit zone vertices at 1 m spacing, southwest to northeast.
  They are not the offline raster's pixel centres. F04 must sample these exact
  coordinates before comparing, not compare equal array indices blindly.
- The manifest includes nine real Perlin samples, the constructor's seven-call RNG
  sequence for each F02 seed, hashes, settings, and plugin GUID/version inventory.
  RNG state is restored synchronously in `finally`, before yielding a frame.
- Water levels are **not captured yet**. The metadata explicitly forbids deriving
  validated land/water classification from this pilot. Full reference acceptance
  requires that missing layer as well as a successful in-game run.
- The manifest's mod DLL hash may differ from the offline DLL because the game uses
  an ILRepack distribution build. Record both artifacts from the same source build;
  do not relax identity checks silently.
- Generated data contains the disposable world's seed and plugin inventory. Logs
  may include local paths. Keep evidence local and review it before sharing.

The JSON writer is reused from the mod via reflection; there is no dependency on
System.Web.Extensions, which is absent from the inspected game installation.

## Targeted precision test

Use the existing disposable world `SPPilotContinents` (seed `AbC123`, Continents).
There is no need to create another world or change its settings. With plugin 0.1.2
loaded, run `sp_reference_pilot` again after the world finishes loading.

The shared observer records IEEE754 float32 hex bits at 18 fixed nodes selected
from the earlier residuals and control points in three zones: scalar settings,
normalization min/max/add/mul, warped coordinates, raw/filtered noise, each layer's
input/output, stack result, offset/sea stages and the real generator's final base
height. It reads state without changing generation code or RNG.

The same observer runs under .NET 10 and standalone Mono for stage-by-stage bit
comparison. Observation calls add call/storage boundaries; the actual generator
result remains authoritative. The observer's final arithmetic stages are not a
claim to reproduce all game rounding behavior. This test determines where they differ.

The exercised trace contains 18 nodes. Against the game, .NET 10 differs in 560 of
890 float observations; the restricted standalone Mono audit differs in 5 of 872
shared observations and lacks the 18 real final callbacks. Its first observed
residual is in layer 3 filtering at `(1009, -480)`, with matching observed raw noise
and normalization parameters. These are diagnostic counts, not acceptance scores.
Exact offline equivalence remains unresolved; water is still absent.

A subsequent local Mono experiment inserted an explicit float32 conversion after
the raw noise call in a disposable mod DLL copy. It matched all 12,675 pilot base
heights and all 872 shared trace observations, repeated in another process and
checked with the .NET parser. The same conversion leaves the .NET 10 discrepancies
unchanged. This restricted experiment does not invoke the full production V3
method and does not establish a supported Mono generator or general parity.
