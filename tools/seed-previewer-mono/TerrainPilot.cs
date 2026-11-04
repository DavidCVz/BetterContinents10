using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using BC=BetterContinents.BetterContinents;

static class TerrainPilot
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    public static int Run(World world,string[] args)
    {
        if(IntPtr.Size!=8)throw new NotSupportedException("Terrain detours require a verified x64 Mono runtime; the x86 instance-double ABI failed isolation tests.");
        if(args.Length<6||args[5]=="-")throw new ArgumentException("Terrain pilot requires a reference directory.");
        var harmonyType=typeof(BC).GetField("HarmonyInstance",All).FieldType;
        var harmony=Activator.CreateInstance(harmonyType,new object[]{"BetterContinents.Harmony"});
        typeof(BC).GetField("HarmonyInstance",All).SetValue(null,harmony);
        foreach(var patch in new[]{typeof(BC.WorldGeneratorPatch),typeof(BC).GetNestedType("AltBiomeControlPatch",All),typeof(BC).GetNestedType("BiomeSectorCanAddModifierPatch",All),typeof(BC).GetNestedType("WorldGeneratorBiomeSectorPatch",All)}){
            if(patch==null)throw new TypeLoadException("Required terrain patch missing.");
            var processor=harmonyType.GetMethod("CreateClassProcessor",new[]{typeof(Type)}).Invoke(harmony,new object[]{patch});
            processor.GetType().GetMethod("Patch",Type.EmptyTypes).Invoke(processor,null);
        }
        BC.DynamicPatch();
        var wanted=(System.Collections.Generic.IEnumerable<string>)typeof(BC).GetMethod("WantedToggles",All).Invoke(null,new object[]{BC.Settings});
        File.WriteAllLines(Path.Combine(args[4],"wanted-patches.txt"),wanted);
        typeof(World).GetField("m_worldGenVersion",All).SetValue(world,2);
        WorldGenerator.Initialize(world);
        var generator=WorldGenerator.instance;
        if(!BiomeContext.InitializeAndCompare(world,generator,args[5],args[4]))return 2;
        File.WriteAllText(Path.Combine(args[4],"terrain-context.json"),SeedPreviewerReference.ReferenceJson.Serialize(new{sectorDataPresent=world.m_biomeData!=null,sectorDataReady=world.m_biomeData!=null&&world.m_biomeData.IsReady,hasBetterContinentsLakes=BC.Settings.HasLakes,limitation="Authored catalog imported; sector assignments generated offline and compared. Full terrain-map acceptance remains pending."}));
        var builder=(HeightmapBuilder)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(HeightmapBuilder));
        var build=typeof(HeightmapBuilder).GetMethod("Build",All);
        try {
            var zones=new[]{new Vector2s(0,0),new Vector2s(16,-7),new Vector2s(-83,59)};
            var data=new System.Collections.Generic.Dictionary<string,HeightmapBuilder.HMBuildData>();
            foreach(var zone in zones){var centre=new UnityEngine.Vector3(zone.x*64,0,zone.y*64);var request=new HeightmapBuilder.HMBuildData(centre,64,1f,false,generator);build.Invoke(builder,new object[]{request});data.Add(zone.x+","+zone.y,request);}
            int points=0,biomeDifferences=0;double max=0,sum=0;
            using(var writer=new StreamWriter(Path.Combine(args[4],"terrain-pilot.tsv"))){
                writer.WriteLine("zoneX\tzoneZ\tx\tz\tbiome\tsurfaceMetres\terrorMetres");
                foreach(var line in File.ReadAllLines(Path.Combine(args[5],"points.tsv")).Skip(1)){
                    var c=line.Split('\t');float x=float.Parse(c[2],CultureInfo.InvariantCulture),z=float.Parse(c[3],CultureInfo.InvariantCulture);
                    var grid=data[c[0]+","+c[1]];int col=(int)(x-int.Parse(c[0])*64+32),row=(int)(z-int.Parse(c[1])*64+32);
                    float surface=grid.m_baseHeights[row*65+col];int biome=(int)generator.GetBiome(x,z);
                    if(biome!=int.Parse(c[5]))biomeDifferences++;double error=Math.Abs((double)surface-float.Parse(c[6],CultureInfo.InvariantCulture));max=Math.Max(max,error);sum+=error;points++;
                    writer.WriteLine(string.Join("\t",c[0],c[1],c[2],c[3],biome,surface.ToString("R",CultureInfo.InvariantCulture),error.ToString("R",CultureInfo.InvariantCulture)));
                }
            }
            File.WriteAllText(Path.Combine(args[4],"terrain-comparison.json"),SeedPreviewerReference.ReferenceJson.Serialize(new{points,biomeDifferences,maxSurfaceErrorMetres=max,meanSurfaceErrorMetres=sum/points,status="terrain-pilot-diagnostic-not-global-acceptance"}));
            System.Console.WriteLine("TERRAIN points="+points+" biomeDifferences="+biomeDifferences+" maxSurfaceError="+max.ToString("R",CultureInfo.InvariantCulture));
            return biomeDifferences==0&&max<=0.1?0:2;
        } finally { /* Build is invoked synchronously; no builder thread was started. */ }
    }
}
