using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using UnityEngine;
using BC = BetterContinents.BetterContinents;

namespace SeedPreviewerReference
{
    // Development only. No terrain patches, world creation, saves, or automatic captures.
    [BepInPlugin("local.seedpreviewer.reference", "Seed Previewer Reference", "0.1.3")]
    [BepInDependency("BetterContinents")]
    public sealed class Plugin : BaseUnityPlugin
    {
        bool registered, busy;
        void Update()
        {
            // m_terminalInstance is an instance property, not a static field. Use the game's console singleton.
            if (registered || global::Console.instance == null) return;
            try
            {
                new Terminal.ConsoleCommand("sp_reference_pilot", "Capture three terrain zones and native primitives for Seed Previewer development", args =>
                {
                    if (busy) { Logger.LogWarning("Capture already running."); return; }
                    if (WorldGenerator.instance == null || ZoneSystem.instance == null || !BC.Settings.ShapesWorld)
                    { Logger.LogWarning("Load a disposable Better Continents test world first."); return; }
                    StartCoroutine(Capture());
                });
                registered = true;
                Logger.LogInfo("Registered sp_reference_pilot.");
            }
            catch (Exception error)
            {
                enabled = false;
                Logger.LogError("Reference command registration failed; disabled further attempts: " + error);
            }
        }

        IEnumerator Capture()
        {
            busy = true;
            var generator = WorldGenerator.instance;
            string output = Path.Combine(Paths.BepInExRootPath, "seed-previewer-reference", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            try
            {
                var prefab = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>();
                if (prefab.m_width != 64 || prefab.m_scale != 1f) throw new NotSupportedException("Pilot supports only 64 m zones at 1 m spacing.");
                var world = (World)typeof(WorldGenerator).GetField("m_world", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(generator);
                var settings = new List<string>();
                BC.Settings.Dump(settings.Add);
                File.WriteAllLines(Path.Combine(output, "effective-settings.txt"), settings);
                File.WriteAllText(Path.Combine(output,"native-primitives.json"),ReferenceJson.Serialize(NativeReference.Capture()));
                var probes = new List<object>();
                foreach (float x in new[] { -1000.25f, 0.125f, 53.875f })
                foreach (float z in new[] { -87.5f, 0.75f, 400.25f })
                    probes.Add(new { x, z, perlin = Mathf.PerlinNoise(x, z) });
                // Preserve the exact engine RNG state; no yield occurs while it is changed.
                var originalState = UnityEngine.Random.state;
                var random = new Dictionary<string, int[]>();
                try
                {
                    foreach (int seed in new[] { 874404622, 874370830, 30859693 })
                    {
                        UnityEngine.Random.InitState(seed);
                        var offsets = new int[7];
                        for (int i = 0; i < 4; i++) offsets[i] = UnityEngine.Random.Range(-10000, 10000);
                        offsets[4] = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                        offsets[5] = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                        offsets[6] = UnityEngine.Random.Range(-10000, 10000);
                        random.Add(seed.ToString(CultureInfo.InvariantCulture), offsets);
                    }
                }
                finally { UnityEngine.Random.state = originalState; }
                var zones = new[] { new Vector2s(0, 0), new Vector2s(16, -7), new Vector2s(-83, 59) };
                var baseHeight = (Func<float, float, bool, float>)Delegate.CreateDelegate(typeof(Func<float, float, bool, float>), generator,
                    typeof(WorldGenerator).GetMethod("GetBaseHeight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
                File.WriteAllText(Path.Combine(output, "precision-trace.json"), ReferenceJson.Serialize(
                    SeedPreviewerDiagnostics.PrecisionTrace.Capture((x, z) => baseHeight(x, z, false))));
                using (var writer = new StreamWriter(Path.Combine(output, "points.tsv")))
                {
                    writer.WriteLine("zoneX\tzoneZ\tx\tz\tbaseHeightNormalized\tbiome\tsurfaceMetres");
                    foreach (var zone in zones)
                    {
                        if (WorldGenerator.instance != generator) throw new InvalidOperationException("World changed during capture.");
                        var centre = ZoneSystem.GetZonePos(zone);
                        // Actual game terrain builder, not the offline adapter or a copy of its blending formula.
                        var data = HeightmapBuilder.instance.RequestTerrainSync(centre, 64, 1f, false, generator);
                        if (data.m_baseHeights.Count != 65 * 65) throw new InvalidDataException("Unexpected terrain grid size.");
                        for (int row = 0; row <= 64; row++)
                        for (int col = 0; col <= 64; col++)
                        {
                            float x = centre.x - 32f + col, z = centre.z - 32f + row;
                            writer.WriteLine(string.Join("\t", zone.x, zone.y, F(x), F(z), F(baseHeight(x, z, false)),
                                (int)generator.GetBiome(x, z), F(data.m_baseHeights[row * 65 + col])));
                        }
                        yield return null;
                    }
                }
                File.WriteAllText(Path.Combine(output, "manifest.json"), ReferenceJson.Serialize(new
                {
                    status = "in-game-pilot-capture-not-offline-parity", format = "sp-reference-pilot/1",
                    seed = world.m_seedName, numericSeed = world.m_seed,
                    gameVersion = Application.version, modVersion = BetterContinents.ModInfo.Version,
                    gameSha256 = Hash(typeof(World).Assembly.Location), modSha256 = Hash(typeof(BC).Assembly.Location),
                    settingsSha256 = Hash(Path.Combine(output, "effective-settings.txt")),
                    pointsSha256 = Hash(Path.Combine(output, "points.tsv")),
                    precisionSha256 = Hash(Path.Combine(output, "precision-trace.json")),
                    nativeSha256 = Hash(Path.Combine(output,"native-primitives.json")),
                    captureVersion = "0.1.3",
                    coordinates = "Explicit X/Z metres; zone nodes row-major +X, then +Z; surface from HeightmapBuilder, no player terrain edits",
                    water = "Ocean baseline in native-primitives.json; per-point volume coverage and water parity remain pending.",
                    perlin = probes, random,
                    randomSequence = "four Range(-10000,10000), two Range(Int32.MinValue,Int32.MaxValue), one Range(-10000,10000); original state restored",
                    plugins = BepInEx.Bootstrap.Chainloader.PluginInfos.Values.Select(p => new { guid = p.Metadata.GUID, version = p.Metadata.Version.ToString() }).ToArray()
                }));
                Logger.LogInfo("Reference pilot captured. Output is under BepInEx/seed-previewer-reference. Review before sharing.");
                if (global::Console.instance != null) global::Console.instance.Print("Reference pilot captured (precision and native probes included).");
            }
            finally { busy = false; }
        }

        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }
    }
}
