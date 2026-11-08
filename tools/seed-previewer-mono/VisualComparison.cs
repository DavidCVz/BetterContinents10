using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEngine;

static class VisualComparison
{
    static string Hash(string p){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(File.ReadAllBytes(p))).Replace("-","");}
    static string Bits(float v)=>BitConverter.ToInt32(BitConverter.GetBytes(v),0).ToString("X8");
    static float Float(JToken t){string b=(string)t["bits"];float v=BitConverter.ToSingle(BitConverter.GetBytes(Convert.ToUInt32(b,16)),0);if(float.IsNaN(v)||float.IsInfinity(v)||Bits((float)t["value"])!=b)throw new InvalidDataException("Invalid reference float bits.");return v;}
    static Color Vertex(WorldGenerator wg,int x,int z,int zoneX,int zoneZ)
    {
        var sw=wg.GetBiome(zoneX*64-32,zoneZ*64-32);var se=wg.GetBiome(zoneX*64+32,zoneZ*64-32);
        var nw=wg.GetBiome(zoneX*64-32,zoneZ*64+32);var ne=wg.GetBiome(zoneX*64+32,zoneZ*64+32);
        wg.GetBiomeHeight(sw,x,z,out var a,false,true);if(sw==se&&sw==nw&&sw==ne)return a;
        wg.GetBiomeHeight(se,x,z,out var b,false,true);wg.GetBiomeHeight(nw,x,z,out var c,false,true);wg.GetBiomeHeight(ne,x,z,out var d,false,true);
        float u=DUtils.SmoothStep(0,1,(float)((double)(x-zoneX*64+32)/64)),v=DUtils.SmoothStep(0,1,(float)((double)(z-zoneZ*64+32)/64));
        return Color.Lerp(Color.Lerp(a,b,u),Color.Lerp(c,d,u),v);
    }
    public static int Run(World world,string[] args)
    {
        string reference=args[5];var manifest=JObject.Parse(File.ReadAllText(Path.Combine(reference,"manifest.json")));
        if((string)manifest["format"]!="sp-visual-reference/1"||(string)manifest["status"]!="complete-native-observations-not-offline-parity"||(string)manifest["seed"]!=args[3]||(string)manifest["preset"]!=args[2]||(int)manifest["numericSeed"]!=world.m_seed||(int)manifest["selectiveVerifiedNodes"]!=100)throw new InvalidDataException("Visual reference identity/contract mismatch.");
        foreach(var file in new[]{new[]{"effective-settings.txt","settingsSha256"},new[]{"biome-context.json","contextSha256"},new[]{"visual-points.json","pointsSha256"}})if(Hash(Path.Combine(reference,file[0]))!=(string)manifest[file[1]])throw new InvalidDataException("Visual reference hash mismatch.");
        if((string)manifest["gameSha256"]!=Hash(Path.Combine(args[0],"tools/seed-previewer/.local/libs/assembly_valheim.dll")))throw new InvalidDataException("Visual game build mismatch.");
        var records=JObject.Parse(File.ReadAllText(Path.Combine(reference,"visual-points.json")));
        if((string)records["format"]!="sp-visual-points/1"||records["observations"].Count()!=(int)manifest["pointCount"]||(int)manifest["pointCount"]<9)throw new InvalidDataException("Incomplete visual observations.");
        var wg=TerrainPilot.Initialize(world,args);if(!BiomeContext.InitializeAndCompare(world,wg,reference,args[4]))throw new InvalidDataException("Sector comparison failed.");
        string Normalize(string s)=>s.Replace("\r\n","\n").Trim();
        if(Normalize(File.ReadAllText(Path.Combine(reference,"effective-settings.txt")))!=Normalize(File.ReadAllText(Path.Combine(args[4],"effective-settings.txt"))))throw new InvalidDataException("Visual settings differ.");
        int differences=0,values=0;var first=new List<string>();
        void Check(bool match,string name){values++;if(!match){differences++;if(first.Count<20)first.Add(name);}}
        void Mask(Color actual,JToken expected){int i=0;foreach(string channel in new[]{"r","g","b","a"}){Float(expected[channel]);Check(Bits(actual[i++])==(string)expected[channel]["bits"],"mask."+channel);}}
        foreach(var p in records["observations"]){
            float x=Float(p["x"]),z=Float(p["z"]);var biome=wg.GetBiome(x,z);var zone=ZoneSystem.GetZone(new Vector3(x,0,z));
            Check((int)biome==(int)p["biome"],"biome");Float(p["forest"]);Check(Bits(WorldGenerator.GetForestFactor(new Vector3(x,0,z)))==(string)p["forest"]["bits"],"forest");
            wg.GetBiomeHeight(biome,x,z,out var mask,false,true);Mask(mask,p["directMask"]);
            var sector=wg.GetBiomeSector(x,z);Check(sector.AltBiomes.Select(a=>a.m_name).SequenceEqual(p["altNames"].Values<string>()),"altNames");
            Check(sector.AltBiomes.Select(a=>(int)a.m_terrainTextureOverride).SequenceEqual(p["textureOverrides"].Values<int>()),"overrideList");
            var alt=sector.AltBiomes.FirstOrDefault(a=>(int)a.m_terrainTextureOverride!=0);var texture=alt==null?sector.Biome:alt.m_terrainTextureOverride;var c=Heightmap.GetBiomeColor(texture);
            Check(new[]{(int)c.r,c.g,c.b,c.a}.SequenceEqual(p["nativeTextureCode"].Values<int>()),"nativeTexturePrecedence");
            if(p["nodes"].Count()!=4)throw new InvalidDataException("Expected four native mask nodes.");
            foreach(var node in p["nodes"])Mask(Vertex(wg,(int)node["x"],(int)node["z"],zone.x,zone.y),node["mask"]);
        }
        File.WriteAllText(Path.Combine(args[4],"visual-comparison.json"),SeedPreviewerReference.ReferenceJson.Serialize(new{status=differences==0?"point-comparison-passed-not-raster-acceptance":"differences",values,differences,first,referenceManifestSha256=Hash(Path.Combine(reference,"manifest.json")),workerSha256=Hash(typeof(VisualComparison).Assembly.Location)}));
        return differences==0?0:2;
    }
}
