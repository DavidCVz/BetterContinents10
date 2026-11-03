# Standalone Mono V3 compatibility probe

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
