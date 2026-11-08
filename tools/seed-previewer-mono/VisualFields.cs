using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEngine;
using BC=BetterContinents.BetterContinents;

// Additional procedural controls, sampled explicitly at the M1 pixel centers.
// These are not a reconstructed GPU terrain texture or object placement map.
static class VisualFields
{
    static string Hash(string p){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(File.ReadAllBytes(p))).Replace("-","");}
    public static int Export(World world,string[] args,string bundle)
    {
        string parent=Path.Combine(bundle,"manifest.json"),parentHash=Hash(parent);
        var manifest=JObject.Parse(File.ReadAllText(parent));var info=JObject.Parse(File.ReadAllText(Path.Combine(bundle,"map/map.json")));
        int n=(int)info["size"];float span=(float)info["totalSizeMetres"];
        if((string)manifest["Status"]!="offline-terrain-map-not-global-game-parity"||(string)manifest["Preset"]!=args[2]||(string)manifest["Seed"]!=args[3]||(int)manifest["Resolution"]!=n||n!=int.Parse(args[6])||span!=BC.TotalSize||(string)info["orientation"]!="north-first pixel centers")throw new InvalidDataException("Visual parent identity mismatch.");
        var sources=new Dictionary<string,string>();
        foreach(string name in new[]{"surface.f32","biomes.i32","below-ocean.u8","map.json"}){
            string hash=Hash(Path.Combine(bundle,"map",name));if((string)manifest["Files"].Single(f=>(string)f["File"]==name)["Sha256"]!=hash)throw new InvalidDataException("Visual parent integrity failure.");sources.Add(name,hash);
        }
        var catalog=JObject.Parse(File.ReadAllText(Path.Combine(args[5],"biome-context.json")));
        if(catalog["definitions"].Count()!=32||catalog["definitions"].Any(d=>(string)d["fields"]["m_terrainTextureOverride"]["bits"]!="00000000"))throw new NotSupportedException("This visual domain requires the validated catalog with no texture overrides.");
        var wg=TerrainPilot.Initialize(world,args);BiomeContext.InitializeAndCompare(world,wg,args[5],args[4],false);
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var classes=new List<string[]> {new string[0]};var classOf=new Dictionary<string,int>{{"",0}};
        int[] known={1,2,4,8,16,32,64,256,512};int changed=0;
        if(new FileInfo(Path.Combine(bundle,"map/biomes.i32")).Length!=(long)n*n*4)throw new InvalidDataException("Invalid parent dimensions.");
        using(var input=new BinaryReader(File.OpenRead(Path.Combine(bundle,"map/biomes.i32"))))
        using(var forest=new BinaryWriter(File.Create(Path.Combine(args[4],"forest.f32"))))
        using(var ground=new BinaryWriter(File.Create(Path.Combine(args[4],"ground.rgba.f32"))))
        using(var variants=new BinaryWriter(File.Create(Path.Combine(args[4],"variants.i32")))){
            for(int row=0;row<n;row++){
                float z=(.5f-(row+.5f)/n)*span;
                for(int col=0;col<n;col++){
                    float x=((col+.5f)/n-.5f)*span;var biome=wg.GetBiome(x,z);int expected=input.ReadInt32();
                    if((int)biome!=expected||!known.Contains(expected))throw new InvalidDataException("Visual biome disagrees with M1.");
                    float f=WorldGenerator.GetForestFactor(new Vector3(x,0,z));wg.GetBiomeHeight(biome,x,z,out var mask,false,true);
                    foreach(float value in new[]{f,mask.r,mask.g,mask.b,mask.a})if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("Non-finite visual control.");
                    forest.Write(f);ground.Write(mask.r);ground.Write(mask.g);ground.Write(mask.b);ground.Write(mask.a);
                    string[] names=wg.GetBiomeSector(x,z).AltBiomes.Select(a=>a.m_name).ToArray();string key=string.Join("\n",names);
                    if(names.Any(s=>s==null||s.Contains("\n")))throw new InvalidDataException("Invalid variant name.");
                    if(!classOf.TryGetValue(key,out int id)){id=classes.Count;classes.Add(names);classOf.Add(key,id);}variants.Write(id);if(id!=0)changed++;
                }
                if(row%128==0)System.Console.WriteLine("VISUAL row="+row+"/"+n);
            }
        }
        foreach(var file in sources)if(Hash(Path.Combine(bundle,"map",file.Key))!=file.Value)throw new IOException("Parent changed during visual export.");
        if(Hash(parent)!=parentHash)throw new IOException("Parent manifest changed.");
        var files=new[]{"forest.f32","ground.rgba.f32","variants.i32"}.ToDictionary(f=>f,f=>Hash(Path.Combine(args[4],f)));
        File.WriteAllText(Path.Combine(args[4],"visual-manifest.json"),SeedPreviewerReference.ReferenceJson.Serialize(new{format="sp-visual-fields/1",status="complete",preset=args[2],seed=args[3],size=n,totalSizeMetres=span,orientation="north-first pixel centers",parentManifestSha256=parentHash,sourceLayers=sources,files,classes,variantPixels=changed,textureOverrides="not-applicable-all-32-authored-definitions-none",sampling="point procedural controls: GetForestFactor(X,0,Z), GetBiomeHeight(GetBiome(X,Z),X,Z). Raw RGBA before texture quantization, terrain edits or GPU filtering; not the blended terrain paint texture.",contextSha256=Hash(Path.Combine(args[5],"biome-context.json")),workerSha256=Hash(typeof(VisualFields).Assembly.Location),samplingMs=watch.ElapsedMilliseconds}));
        return 0;
    }
}
