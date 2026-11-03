using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using HarmonyLib;
using UnityEngine;

// Invoke the installed engine bindings in an isolated process; never substitute noise or RNG.
internal static class NativeTerrainProbe
{
    internal static int Run(string[] paths)
    {
        var probes = new[]
        {
            Test(typeof(Mathf).GetMethod("PerlinNoise", new[] { typeof(float), typeof(float) })!, new object[] { 0.125f, 0.75f }),
            Test(typeof(UnityEngine.Random).GetMethod("InitState")!, new object[] { 874404622 }),
            Test(typeof(UnityEngine.Random).GetMethod("Range", new[] { typeof(int), typeof(int) })!, new object[] { -10000, 10000 }),
            Test(typeof(UnityEngine.Random).GetProperty("state")!.GetMethod!, Array.Empty<object>())
        };
        var roots = typeof(WorldGenerator).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Where(m => m.Name is "GetBiome" or "GetBiomeHeight" or "GetMeadowsHeight" or "GetSnowMountainHeight")
            .Cast<MethodBase>().Concat(typeof(WorldGenerator).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
        var calls = roots.Select(root => new
        {
            method = root.ToString(),
            directCalls = PatchProcessor.GetOriginalInstructions(root).Select(i => i.operand).OfType<MethodBase>()
                .Select(m => $"{m.DeclaringType?.FullName}.{m}").Distinct().ToArray()
        }).ToArray();
        bool available = probes.All(p => p.Succeeded);
        System.Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = available ? "native-bindings-callable-not-yet-validated" : "native-bindings-unavailable",
            runtime = Environment.Version.ToString(),
            gameSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(paths[0], "assembly_valheim.dll")))) ,
            unitySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(paths[0], "UnityEngine.CoreModule.dll")))),
            probes, calls,
            limitation = "No biome or surface map is emitted. Callable bindings alone would not prove parity."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return available ? 0 : 2;
    }

    static ProbeResult Test(MethodInfo method, object[] args)
    {
        try
        {
            var value = method.Invoke(null, args);
            return new(method.ToString()!, true, method.GetMethodImplementationFlags().ToString(), value?.ToString(), null, null);
        }
        catch (Exception error)
        {
            while (error.InnerException != null) error = error.InnerException;
            return new(method.ToString()!, false, method.GetMethodImplementationFlags().ToString(), null, error.GetType().FullName, error.Message);
        }
    }

    sealed record ProbeResult(string Method, bool Succeeded, string Implementation, string? Value, string? ErrorType, string? Error);
}
