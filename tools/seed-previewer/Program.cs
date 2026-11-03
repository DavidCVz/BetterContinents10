using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using UnityEngine;

// Independent process: read presets (F01), or sample real base height (F02).
internal static class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length < 4)
                throw new ArgumentException("Usage: SeedPreviewer <libs-directory> <mod-dll> <mod-dependencies-directory> <preset-file>");
            var paths = args.Take(4).Select(Path.GetFullPath).ToArray();
            foreach (var file in new[] { "UnityEngine.CoreModule.dll", "assembly_valheim.dll", "assembly_utils.dll", "BepInEx.dll" })
                if (!File.Exists(Path.Combine(paths[0], file)))
                    throw new FileNotFoundException($"Missing dependency: {file} in {paths[0]}");
            if (!File.Exists(paths[1])) throw new FileNotFoundException("Build the mod first", paths[1]);
            if (!File.Exists(paths[3])) throw new FileNotFoundException("Preset not found", paths[3]);
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                foreach (var dir in new[] { AppContext.BaseDirectory, paths[0], paths[2] })
                {
                    var candidate = Path.Combine(dir, name.Name + ".dll");
                    if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
                }
                return null;
            };
            AssemblyLoadContext.Default.LoadFromAssemblyPath(paths[1]);
            if (args.Length == 5 && args[4] == "--inspect-game") return GameInspector.Run(paths);
            if (args.Length == 8 && args[4] == "--height")
            {
                if (args[5].Length is < 1 or > 10 || !args[5].All(char.IsAsciiLetterOrDigit))
                    throw new ArgumentException("This milestone supports 1-10 ASCII letters/digits for a seed.");
                if (!int.TryParse(args[6], out int size) || size < 64 || size > 2048)
                    throw new ArgumentException("Resolution must be between 64 and 2048.");
                return HeightGenerator.Run(paths, args[5], size, Path.GetFullPath(args[7]));
            }
            if (args.Length != 4) throw new ArgumentException("Expected four paths, optionally --height <seed> <resolution> <new-output-directory>.");
            return ReadPreset(paths);
        }
        catch (Exception error)
        {
            while (error.InnerException != null) error = error.InnerException;
            System.Console.Error.WriteLine($"ERROR {error.GetType().Name}: {error.Message}");
            System.Console.Error.WriteLine(error.StackTrace);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int ReadPreset(string[] paths)
    {
        // Managed log sink, as in the existing offline harnesses. No native logging call.
        UnityEngine.Debug.unityLogger.logHandler = new ConsoleLog();
        var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(paths[1]);
        var type = mod.GetType("BetterContinents.BetterContinents+BetterContinentsSettings", throwOnError: true)!;
        var settings = type.GetMethod("Load", new[] { typeof(string) })!.Invoke(null, new object[] { paths[3] })!;
        object? Value(string name) => type.GetField(name)?.GetValue(settings) ?? type.GetProperty(name)?.GetValue(settings);
        System.Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "preset-read-only",
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            modVersion = mod.GetType("BetterContinents.ModInfo")!.GetField("Version")!.GetRawConstantValue(),
            preset = Path.GetFileName(paths[3]),
            presetSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths[3]))),
            version = Value("Version"),
            enabled = Value("EnabledForThisWorld"),
            worldSize = Value("WorldSize"),
            edgeSize = Value("EdgeSize"),
            gameAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == "assembly_valheim")?.GetName().Version?.ToString()
        }));
        return 0;
    }
}

internal sealed class ConsoleLog : ILogHandler
{
    public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args) =>
        System.Console.Error.WriteLine($"[{logType}] " + string.Format(format, args));
    public void LogException(Exception exception, UnityEngine.Object context) => System.Console.Error.WriteLine(exception);
}
