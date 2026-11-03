using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using BC = BetterContinents.BetterContinents;

internal static class BaseReferenceComparison
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    internal static int Run(string reference, string output, string seed, int numericSeed,
        BC.BetterContinentsSettings settings, float minMountainDistance, string[] paths)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(reference, "manifest.json")));
        var manifest = document.RootElement;
        void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        Require(manifest.GetProperty("format").GetString() == "sp-reference-pilot/1", "Unsupported reference format.");
        Require(manifest.GetProperty("seed").GetString() == seed && manifest.GetProperty("numericSeed").GetInt32() == numericSeed, "Reference seed mismatch.");
        Require(Hash(Path.Combine(reference, "points.tsv")) == manifest.GetProperty("pointsSha256").GetString(), "Reference point hash mismatch.");
        Require(Hash(Path.Combine(reference, "effective-settings.txt")) == manifest.GetProperty("settingsSha256").GetString(), "Reference settings hash mismatch.");
        Require(Hash(Path.Combine(paths[0], "assembly_valheim.dll")) == manifest.GetProperty("gameSha256").GetString(), "Reference game DLL mismatch.");
        Require(Hash(Path.Combine(paths[2], "BetterContinents.dll")) == manifest.GetProperty("modSha256").GetString(), "Reference distribution mod DLL mismatch.");
        var plugins = manifest.GetProperty("plugins").EnumerateArray().Select(p => p.GetProperty("guid").GetString()).Order().ToArray();
        Require(plugins.SequenceEqual(new[] { "BetterContinents", "local.seedpreviewer.reference" }.Order()), "Unexpected plugins in reference.");
        var dump = new List<string>(); settings.Dump(dump.Add);
        File.WriteAllLines(Path.Combine(output, "effective-settings.txt"), dump);
        // Dump uses runtime-default float formatting; Mono prints fewer digits than .NET 10.
        // This supplemental textual check is not a substitute for package/float-bit provenance.
        static string Normalize(string value) => Regex.Replace(value.Replace("\r\n", "\n").Trim(),
            @"(?<![A-Za-z])[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?", m => float.Parse(m.Value, Invariant).ToString("G7", Invariant));
        bool settingsMatch = Normalize(string.Join("\n", dump)) == Normalize(File.ReadAllText(Path.Combine(reference, "effective-settings.txt")));
        Require(settingsMatch, "Effective settings differ from the reference after accounting for Mono G7 display formatting.");
        var groups = new Dictionary<string, List<double>>();
        int count = 0, bitDifferences = 0;
        using var reader = new StreamReader(Path.Combine(reference, "points.tsv"));
        Require(reader.ReadLine() == "zoneX\tzoneZ\tx\tz\tbaseHeightNormalized\tbiome\tsurfaceMetres", "Unexpected reference columns.");
        using (var writer = new StreamWriter(Path.Combine(output, "base-comparison.tsv")))
        {
            writer.WriteLine("zoneX\tzoneZ\tx\tz\treferenceBase\tofflineBase\terrorMetres\treferenceBits\tofflineBits");
            while (reader.ReadLine() is { } line)
            {
                var cells = line.Split('\t'); Require(cells.Length == 7, "Malformed reference point.");
                float x = float.Parse(cells[2], Invariant), z = float.Parse(cells[3], Invariant);
                float expected = float.Parse(cells[4], Invariant), actual = 0;
                Require(float.IsFinite(x) && float.IsFinite(z) && float.IsFinite(expected), "Non-finite reference point.");
                BC.WorldGeneratorPatch.GetBaseHeightPrefixV3(ref x, ref z, ref actual, minMountainDistance);
                Require(float.IsFinite(actual), "Non-finite sampled height.");
                double error = Math.Abs((double)actual - expected) * 200.0;
                string key = cells[0] + "," + cells[1];
                if (!groups.TryGetValue(key, out var errors)) groups.Add(key, errors = new());
                errors.Add(error); count++;
                int expectedBits = BitConverter.SingleToInt32Bits(expected), actualBits = BitConverter.SingleToInt32Bits(actual);
                if (expectedBits != actualBits) bitDifferences++;
                writer.WriteLine(string.Join("\t", cells[0], cells[1], cells[2], cells[3], expected.ToString("R", Invariant), actual.ToString("R", Invariant),
                    error.ToString("R", Invariant), expectedBits.ToString("X8"), actualBits.ToString("X8")));
            }
        }
        Require(count == 12675 && groups.Count == 3 && groups.Values.All(v => v.Count == 4225), "Incomplete pilot grid.");
        double max = groups.Values.SelectMany(v => v).Max();
        File.WriteAllText(Path.Combine(output, "base-comparison.json"), JsonSerializer.Serialize(new
        {
            status = bitDifferences == 0 ? "base-height-exact-at-pilot-nodes" : "base-height-discrepancies-require-analysis",
            seed, numericSeed, settingsVersion = settings.Version, settingsDisplayMatchesAtG7 = settingsMatch,
            points = count, bitDifferences, maxErrorMetres = max,
            meanErrorMetres = groups.Values.SelectMany(v => v).Average(),
            zones = groups.Select(g => new { zone = g.Key, points = g.Value.Count, maxErrorMetres = g.Value.Max(), meanErrorMetres = g.Value.Average() }),
            referencePointSha256 = Hash(Path.Combine(reference, "points.tsv")),
            limitation = "Base height only. This is not biome, surface, water or full-world fidelity acceptance."
        }, new JsonSerializerOptions { WriteIndented = true }));
        System.Console.WriteLine($"BASE comparison: {count} points, {bitDifferences} bit differences, max error {max.ToString("R", Invariant)} m");
        return bitDifferences == 0 ? 0 : 2;
    }
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
