using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using BepInEx.Configuration;
using HarmonyLib;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using BC = BetterContinents.BetterContinents;

internal static class HeightGenerator
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Run(string[] paths, string seed, int size, string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be a new directory.");
        var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(paths[1]);
        if (typeof(BC).Assembly != mod) throw new InvalidOperationException("Unexpected mod assembly.");
        UnityEngine.Debug.unityLogger.logHandler = new ConsoleLog();
        var timer = Stopwatch.StartNew();
        Directory.CreateDirectory(output);
        // No local player's config is read. Declare the exact mod defaults in memory.
        var config = new ConfigFile(Path.Combine(output, "defaults.cfg"), false) { SaveOnConfigSet = false };
        typeof(BC).GetMethod("DeclareConfig", All)!.Invoke(null, new object[] { config });
        var build = mod.GetType("BetterContinents.NewWorldBuild")!;
        var choiceType = build.GetNestedType("Choice", All)!;
        var choice = Activator.CreateInstance(choiceType, nonPublic: true)!;
        choiceType.GetField("PresetPath", All)!.SetValue(choice, paths[3]);
        choiceType.GetField("WideSectors", All)!.SetValue(choice, BC.ConfigWideSectors.Value);
        var settings = (BC.BetterContinentsSettings)build.GetMethod("Build", All)!.Invoke(null, new[] { choice })!;
        if (!settings.ShapesWorld || settings.Version < 7 || settings.BaseHeightNoise.NoiseLayers.Count == 0)
            throw new NotSupportedException("Only real V3 preset worlds with noise layers are supported.");
        // These fixtures have no imported maps. Reject other inputs until their full provenance is supported.
        if (!new[] { "Continents", "Pangaea", "Swirls", "Vanillaish" }.Contains(Path.GetFileNameWithoutExtension(paths[3])))
            throw new NotSupportedException("Only the four shipped preset fixtures are supported in F02.");
        var shipped = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../assets/BCAssets/Presets", Path.GetFileName(paths[3])));
        if (!File.ReadAllBytes(paths[3]).SequenceEqual(File.ReadAllBytes(shipped)))
            throw new NotSupportedException("Preset bytes differ from the shipped fixture.");

        BC.Settings = settings;
        typeof(BC).GetMethod("UpdateGeometry", All)!.Invoke(null, null);
        var worldType = typeof(World);
        // Verify the installed game's seed conversion call, then invoke that actual managed method.
        var constructor = worldType.GetConstructor(new[] { typeof(string), typeof(string) })!;
        var calls = PatchProcessor.GetOriginalInstructions(constructor);
        var hashMethod = calls.Select(i => i.operand).OfType<MethodInfo>()
            .First(m => m.Name == "GetStableHashCode" && m.DeclaringType?.Name == "StringExtensionMethods");
        int numericSeed = (int)hashMethod.Invoke(null, new object[] { seed })!;
        // Independently check the conversion for the supported ASCII input against the game's implementation.
        if (numericSeed != StableHash(seed)) throw new InvalidOperationException("Seed conversion mismatch.");
        var world = new World();
        worldType.GetField("m_seed", All)!.SetValue(world, numericSeed);
        worldType.GetField("m_menu", All)!.SetValue(world, false);
        typeof(BC.WorldGeneratorPatch).GetMethod("InitializePrefix", All)!.Invoke(null, new object[] { world });

        // Read, rather than assume, the game's initial mountain restriction. VersionSetup must be audited separately.
        var wgCtor = typeof(WorldGenerator).GetConstructor(All, null, new[] { worldType }, null)!;
        var instructions = PatchProcessor.GetOriginalInstructions(wgCtor);
        int index = instructions.FindIndex(i => i.operand is FieldInfo f && f.Name == "m_minMountainDistance");
        if (index < 1 || instructions[index - 1].operand is not float minMountainDistance || minMountainDistance != 1000f)
            throw new NotSupportedException("Unexpected mountain-distance initialization in the installed game.");
        var worldInstructions = PatchProcessor.GetOriginalInstructions(constructor);
        int versionIndex = worldInstructions.FindIndex(i => i.operand is FieldInfo f && f.Name == "m_worldGenVersion");
        if (versionIndex < 1 || worldInstructions[versionIndex - 1].opcode != System.Reflection.Emit.OpCodes.Ldc_I4_2)
            throw new NotSupportedException("This prototype is audited only for world-generation version 2.");
        var setup = RuntimeHelpers.GetUninitializedObject(typeof(WorldGenerator));
        var mountainField = typeof(WorldGenerator).GetField("m_minMountainDistance", All)!;
        mountainField.SetValue(setup, minMountainDistance);
        typeof(WorldGenerator).GetMethod("VersionSetup", All)!.Invoke(setup, new object[] { 2 });
        minMountainDistance = (float)mountainField.GetValue(setup)!;

        float total = BC.TotalSize;
        var values = new float[size * size];
        float min = float.MaxValue, max = float.MinValue;
        for (int row = 0; row < size; row++)
        for (int col = 0; col < size; col++)
        {
            float x = ((col + 0.5f) / size - 0.5f) * total;
            float z = (0.5f - (row + 0.5f) / size) * total;
            float value = 0;
            BC.WorldGeneratorPatch.GetBaseHeightPrefixV3(ref x, ref z, ref value, minMountainDistance);
            if (!float.IsFinite(value)) throw new InvalidDataException($"Non-finite height at {col},{row}.");
            values[row * size + col] = value;
            min = Math.Min(min, value); max = Math.Max(max, value);
        }
        var binary = Path.Combine(output, "height-base.f32");
        using (var writer = new BinaryWriter(File.Create(binary))) foreach (float value in values) writer.Write(value);
        using (var image = new Image<L16>(size, size))
        {
            for (int row = 0; row < size; row++)
            for (int col = 0; col < size; col++)
                image[col, row] = new L16(max == min ? (ushort)0 : (ushort)Math.Clamp((int)((values[row * size + col] - min) / (max - min) * 65535f), 0, 65535));
            image.SaveAsPng(Path.Combine(output, "height-base.png"));
        }
        var options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
        settings.Dump(line => File.AppendAllText(Path.Combine(output, "effective-settings.txt"), line + Environment.NewLine));
        File.WriteAllText(Path.Combine(output, "default-config.json"), JsonSerializer.Serialize(config.ToDictionary(k => k.Key.ToString(), v => v.Value.BoxedValue?.ToString()), options));
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
        {
            status = "base-height-only-unvalidated-against-game", seed, numericSeed, size, totalSizeMetres = total,
            preset = Path.GetFileName(paths[3]), presetSha256 = Hash(paths[3]), settingsVersion = settings.Version,
            modVersion = BetterContinents.ModInfo.Version, modSha256 = Hash(paths[1]), worldGenerationVersion = 2,
            gameSha256 = Hash(Path.Combine(paths[0], "assembly_valheim.dll")), hashAssemblySha256 = Hash(hashMethod.Module.Assembly.Location),
            seedHashMethod = hashMethod.ToString(), runtime = Environment.Version.ToString(), minMountainDistance,
            coordinates = "row-major pixel centres; x=((col+0.5)/size-0.5)*total; z=(0.5-(row+0.5)/size)*total",
            encoding = "height-base.f32: IEEE754 float32 little-endian, base height in game normalized units; metres=value*200; not final surface",
            image = "16-bit grayscale, display-only linear normalization between min and max; not a coastline map",
            min, max, dataSha256 = Hash(binary), elapsedMs = timer.ElapsedMilliseconds,
            peakMemoryBytes = Process.GetCurrentProcess().PeakWorkingSet64
        }, options));
        System.Console.WriteLine($"OK {Path.GetFileName(paths[3])} seed={seed} numeric={numericSeed} size={size} sha256={Hash(binary)}");
        return 0;
    }

    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static int StableHash(string text)
    {
        unchecked
        {
            int hash1 = 5381, hash2 = 5381;
            for (int i = 0; i < text.Length; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ text[i];
                if (i + 1 >= text.Length) break;
                hash2 = ((hash2 << 5) + hash2) ^ text[i + 1];
            }
            return hash1 + hash2 * 1566083941;
        }
    }
}
