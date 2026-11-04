using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;
using BC=BetterContinents.BetterContinents;

static class GlobalComparison
{
    sealed class Stats
    {
        public List<double> Errors=new List<double>();public int Biomes,Inside,InsideDifferences,Outside,OutsideDifferences;
        public void Add(double error,bool biome,float expected,float actual,int below){Errors.Add(error);if(!biome)Biomes++;if(Math.Abs((double)expected-30)<=.1){Inside++;if((actual<30?1:0)!=below)InsideDifferences++;}else{Outside++;if((actual<30?1:0)!=below)OutsideDifferences++;}}
        public object Report(){var sorted=Errors.OrderBy(e=>e).ToArray();return new{count=sorted.Length,max=sorted.Max(),mean=sorted.Average(),p95=sorted[(int)Math.Ceiling(sorted.Length*.95)-1],biomeDifferences=Biomes,waterInside=Inside,waterInsideDifferences=InsideDifferences,waterOutside=Outside,waterOutsideDifferences=OutsideDifferences};}
        public bool Pass=>Errors.Count>0&&Errors.Max()<=.1&&Biomes==0&&OutsideDifferences==0;
    }
    static float Parse(string s){float f=float.Parse(s,CultureInfo.InvariantCulture);if(float.IsNaN(f)||float.IsInfinity(f))throw new InvalidDataException("Non-finite reference coordinate/value.");return f;}
    static string Bits(float f)=>BitConverter.ToInt32(BitConverter.GetBytes(f),0).ToString("X8");
    public static int Run(World world,string[] args)
    {
        var m=JObject.Parse(File.ReadAllText(Path.Combine(args[5],"manifest.json")));
        if((string)m["preset"]!=args[2]||(string)m["seed"]!=args[3]||(int)m["numericSeed"]!=world.m_seed||(int)m["worldVersion"]!=2||(int)m["size"]!=256||(float)m["totalSizeMetres"]!=BC.TotalSize)throw new InvalidDataException("Global reference identity mismatch.");
        string expected=File.ReadAllText(Path.Combine(args[5],"effective-settings.txt")).Replace("\r\n","\n").Trim();
        string actual=File.ReadAllText(Path.Combine(args[4],"effective-settings.txt")).Replace("\r\n","\n").Trim();
        if(expected!=actual)throw new InvalidDataException("Global reference effective settings mismatch.");
        if(BC.Settings.HasLakes)throw new NotSupportedException("Only static ocean reference supported.");
        var context=JObject.Parse(File.ReadAllText(Path.Combine(args[5],"biome-context.json")));
        if((bool)context["water"]["hasBetterContinentsLakes"]||(string)context["water"]["baselineMetres"]["bits"]!="41F00000"||(string)context["water"]["prefabWater"]?["worldY"]?["bits"]!="41F00000")throw new InvalidDataException("Global water contract mismatch.");
        MeshSurface.VerifyTopology(Path.Combine(args[5],"mesh-triangles.i32"));
        var generator=TerrainPilot.Initialize(world,args);BiomeContext.InitializeAndCompare(world,generator,args[5],args[4],false);
        var all=new Stats();var groups=new Dictionary<string,Stats>();foreach(string kind in new[]{"grid","coast","transition","anchor"})groups.Add(kind,new Stats());
        var seen=new HashSet<string>();int index=0;var cache=new Dictionary<string,SparseSurface>();float span=BC.TotalSize;
        if(File.ReadLines(Path.Combine(args[5],"points.tsv")).First()!="kind\trow\tcol\tx\tz\tbiome\tsurfaceMetres\tbelowOceanPlane")throw new InvalidDataException("Invalid global reference header.");
        using(var writer=new StreamWriter(Path.Combine(args[4],"comparison.tsv"))){
            writer.WriteLine("kind\trow\tcol\tx\tz\tbiome\tsurfaceMetres\terrorMetres\tbelowOceanPlane");
            foreach(var line in File.ReadLines(Path.Combine(args[5],"points.tsv")).Skip(1)){
                var c=line.Split('\t');if(c.Length!=8||!groups.ContainsKey(c[0]))throw new InvalidDataException("Invalid global point schema.");
                int row=int.Parse(c[1]),col=int.Parse(c[2]);float x=Parse(c[3]),z=Parse(c[4]),height=Parse(c[6]);
                if(Math.Abs(x)>span/2||Math.Abs(z)>span/2||!seen.Add(Bits(x)+","+Bits(z)))throw new InvalidDataException("Invalid or duplicate global coordinate.");
                if(index<65536){
                    if(c[0]!="grid"||row!=index/256||col!=index%256||Bits(x)!=Bits(((col+.5f)/256-.5f)*span)||Bits(z)!=Bits((.5f-(row+.5f)/256)*span))throw new InvalidDataException("Global grid coordinate contract mismatch.");
                }else if(c[0]=="grid"||row!=-1||col!=-1)throw new InvalidDataException("Invalid extra point coordinates.");
                if(c[0]=="anchor"){
                    int anchor=groups["anchor"].Errors.Count;
                    if(anchor>=9||Bits(x)!=Bits((anchor/3-1)*(span/2))||Bits(z)!=Bits((anchor%3-1)*(span/2)))throw new InvalidDataException("Global anchor coordinate contract mismatch.");
                }
                int below=int.Parse(c[7]);if(below!=(height<30?1:0))throw new InvalidDataException("Global reference water mismatch.");
                var zone=ZoneSystem.GetZone(new Vector3(x,0,z));string key=zone.x+","+zone.y;
                if(!cache.TryGetValue(key,out var sampler)){if(cache.Count>=512)cache.Clear();sampler=new SparseSurface(generator,zone.x,zone.y);cache.Add(key,sampler);}
                float surface=sampler.Sample(x,z);int biome=(int)generator.GetBiome(x,z);double error=Math.Abs((double)surface-height);
                all.Add(error,biome==int.Parse(c[5]),height,surface,below);groups[c[0]].Add(error,biome==int.Parse(c[5]),height,surface,below);
                writer.WriteLine(string.Join("\t",c[0],c[1],c[2],c[3],c[4],biome,surface.ToString("R",CultureInfo.InvariantCulture),error.ToString("R",CultureInfo.InvariantCulture),surface<30?1:0));index++;
            }
        }
        if(index!=65645||index!=(int)m["count"]||groups["grid"].Errors.Count!=65536||groups["coast"].Errors.Count!=50||groups["transition"].Errors.Count!=50||groups["anchor"].Errors.Count!=9)throw new InvalidDataException("Incomplete global reference groups.");
        File.WriteAllText(Path.Combine(args[4],"comparison.json"),SeedPreviewerReference.ReferenceJson.Serialize(new{status=all.Pass?"global-combination-passed":"global-combination-differences",preset=args[2],seed=args[3],all=all.Report(),groups=groups.ToDictionary(p=>p.Key,p=>p.Value.Report()),surfaceToleranceMetres=.1,waterBandMetres=.1,limitation="One combination; native plane reference, static ocean only. Full matrix and performance remain separate."}));
        return all.Pass?0:2;
    }
}
