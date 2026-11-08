using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace SeedPreviewer.Desktop;

internal sealed record PreviewResult(string ImagePath, string LegendPath);

internal static class PreviewJob
{
    internal static string FindToolDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Generate-Preview.ps1")))
                return directory.FullName;
        }
        throw new IOException("Abre la aplicación desde su carpeta dentro del repositorio.");
    }

    internal static async Task<PreviewResult> RunAsync(
        string toolDirectory, string output, string preset, string seed, int resolution,
        IProgress<string> progress, CancellationToken cancellation)
    {
        var settings = Path.Combine(toolDirectory, ".local", "workspace.json");
        if (!File.Exists(settings))
            throw new IOException("Falta .local/workspace.json. Consulta la preparación en el README.");

        var start = new ProcessStartInfo("pwsh.exe")
        {
            WorkingDirectory = toolDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (string argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-File", Path.Combine(toolDirectory, "Generate-Preview.ps1"),
            "-Settings", settings, "-Output", output, "-Preset", preset, "-Seed", seed,
            "-Resolution", resolution.ToString(System.Globalization.CultureInfo.InvariantCulture)
        })
            start.ArgumentList.Add(argument);

        cancellation.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar PowerShell 7.");
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = ObserveProgress(process.StandardOutput, progress);
        try
        {
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            throw;
        }
        await stdout.ConfigureAwait(false);
        string error = await stderr.ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            if (Directory.Exists(output))
                await File.WriteAllTextAsync(Path.Combine(output, "job-error.log"), error).ConfigureAwait(false);
            throw new IOException(Directory.Exists(output)
                ? "La generación falló. Abre la carpeta de salida para revisar los registros."
                : "No se pudo preparar el entorno. Comprueba el archivo de configuración y PowerShell 7.");
        }

        var render = Path.Combine(output, "render");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(render, "render-manifest.json"), cancellation).ConfigureAwait(false));
        var root = manifest.RootElement;
        if (root.GetProperty("preset").GetString() != preset || root.GetProperty("seed").GetString() != seed ||
            root.GetProperty("size").GetInt32() != resolution ||
            root.GetProperty("mode").GetString() != "procedural-visual-controls")
            throw new InvalidDataException("El render recibido no coincide con la solicitud.");
        var image = VerifyOutput(render, "map.png", root);
        var legend = VerifyOutput(render, "legend.json", root);
        return new PreviewResult(image, legend);
    }

    private static string VerifyOutput(string directory, string name, JsonElement manifest)
    {
        string path = Path.Combine(directory, name);
        using var file = File.OpenRead(path);
        string hash = Convert.ToHexString(SHA256.HashData(file));
        if (manifest.GetProperty("outputs").GetProperty(name).GetString() != hash)
            throw new InvalidDataException("Un archivo del mapa no coincide con su manifiesto.");
        return path;
    }

    private static async Task ObserveProgress(StreamReader reader, IProgress<string> progress)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            if (line.StartsWith("STAGE|", StringComparison.Ordinal))
                progress.Report(line["STAGE|".Length..]);
    }
}
