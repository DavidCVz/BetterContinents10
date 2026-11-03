using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

internal static class GameInspector
{
    internal static int Run(string[] paths)
    {
        var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(paths[0], "assembly_valheim.dll"));
        foreach (var typeName in new[] { "World", "WorldGenerator" })
        {
            var type = game.GetType(typeName)!;
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.Name.Contains("Seed") || m.Name == "Initialize" || m.Name == "VersionSetup");
            foreach (var method in methods.Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)))
            {
                System.Console.WriteLine($"METHOD {typeName}.{method}");
                foreach (var instruction in PatchProcessor.GetOriginalInstructions(method)) System.Console.WriteLine(instruction);
            }
        }
        return 0;
    }
}
