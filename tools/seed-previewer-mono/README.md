# Standalone Mono V3 compatibility probe

## Experimental F03 terrain pilot

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
./Run-TerrainPilot.ps1 -Mono path/to/mono-x64/bin/mono.exe -Bootstrap .local/new-terrain -Reference path/to/capture-0.1.3 -Output .local/new-terrain-run
```

The runner scopes MONO_PATH to that runtime's class libraries and uses
`--optimize=-float32`. This flag is necessary for base-noise parity; the default
x64 arithmetic retains the earlier 8,067 base-height differences. Explicit
float32 boundaries in the primitive candidates remain necessary as well.

The terrain bootstrap changes only copied Perlin/RNG bindings to NativeBridge,
uses original game formulas and the real WorldGeneratorPatch/DynamicPatch paths,
and invokes actual HeightmapBuilder.Build synchronously with its real request
constructor. It does not start the builder thread or reproduce its blend formula.
Two runs match all 12,675 pilot biomes and surface values exactly. This is a pilot,
not CA-04/global parity acceptance: full static-patch coverage, twelve-map output
and water classification remain pending. Logs still contain unresolved Quaternion
binding warnings while vegetation patch methods are prepared; these are not
silenced with synthetic rotations and must be addressed if a required path uses them.
Native failures in experimental modes retain logs/exit codes without Windows
crash dialogs. Original DLLs and the game profile are unchanged.

F03 investigation: `UnityIntegerRandomCandidate.cs` and `UnityPerlinCandidate.cs`
are separate candidates, not wired into the terrain worker or Unity's APIs.
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
