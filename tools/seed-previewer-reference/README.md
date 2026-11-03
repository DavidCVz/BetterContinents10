# In-game reference pilot — development only

Status: reference plugin 0.1.1 was exercised in-game. Version 0.1.2 adds shared
float-bit diagnostics; it is compiled and offline-tested, pending its first real
precision capture. This is a validation tool, not an offline generator or a
solution to the native binding gate.

Build after `../seed-previewer/Prepare.ps1` has prepared the dependencies:

```powershell
dotnet build SeedPreviewerReference.csproj -c Release
```

Use the same workspace-local NuGet/CLI cache as the preparation script if needed.
The only output to deploy for this tool is `bin/Release/net48/SeedPreviewerReference.dll`.
It relies on the game's BepInEx and Better Continents; do not copy reference/game DLLs.

## Pilot procedure (pending execution)

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
   is written after a failure. The added precision capture still requires in-game validation.
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
