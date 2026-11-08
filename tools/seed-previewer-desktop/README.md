# Better Continents Seed Previewer

Windows desktop application for inspecting seeds with the A Actual style. The app uses the existing terrain, visual field and map rendering tools in separate processes. It supports Continents, Pangaea, Swirls and Vanillaish with 512² and 1024² maps.

## Run in a prepared workspace

Use PowerShell 7 and the .NET 10 Desktop Runtime. To build, use the .NET 10 SDK.

```powershell
dotnet build tools/seed-previewer-desktop/SeedPreviewer.Desktop.csproj -c Release
& ./tools/seed-previewer-desktop/bin/Release/net10.0-windows/SeedPreviewer.Desktop.exe
```

Enter a seed or click **Aleatoria** to fill the editable field with ten characters drawn from the alphabet used by Valheim's seed generator. Choose the preset and click **Generar mapa** when ready. Scroll to zoom, drag to pan, and use **Ajustar** to fit the map. **Guardar PNG** copies the generated map at its original resolution. Each run has its own directory under `.local/runs`. **Abrir resultados** opens the last run with its logs and manifests.

## Development workspace configuration

Build the Mono worker and .NET renderer in Release. Prepare the local game assemblies, Mono x64 runtime, terrain bootstrap and reference evidence using the existing tool instructions. The successful visual comparison must match the worker binary, runtime and bootstrap selected for this workspace. See [worker preparation](../seed-previewer-mono/README.md) and [renderer preparation](../seed-previewer/README.md).

Create `.local/workspace.json` within this tool directory. Use absolute paths or paths relative to the repository root.

```json
{
  "format": "seed-previewer-workspace/1",
  "mono": "path/to/mono-x64/bin/mono.exe",
  "bootstrap": "path/to/terrain-bootstrap",
  "pilot": "path/to/verified-fractional-reference",
  "visualReference": "path/to/visual-reference",
  "visualComparison": "path/to/successful-visual-comparison"
}
```

The initial desktop build targets a prepared development workspace. The reference data and the local configuration belong under ignored paths or outside the repository.
