using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using BepInEx;
using UnityEngine;
using BC=BetterContinents.BetterContinents;

namespace SeedPreviewerReference
{
    // Native observations only: no candidate mask interpolation or artistic transfer.
    internal static class VisualReference
    {
        static object F(float v)=>SeedPreviewerDiagnostics.PrecisionTrace.Float(v);
        static object Mask(Color c)=>new{r=F(c.r),g=F(c.g),b=F(c.b),a=F(c.a)};
        static string Hash(string p){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(File.ReadAllBytes(p))).Replace("-","");}
        public static IEnumerator Capture(Action finished)
        {
            string output=Path.Combine(Paths.BepInExRootPath,"seed-previewer-visual-reference",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            try{
                var wg=WorldGenerator.instance;var world=wg.m_world;
                if(world.m_seedName!="AbC123"||BC.Settings.HasLakes)throw new NotSupportedException("Load the disposable Continents/AbC123 world, static ocean only.");
                var settings=new List<string>();BC.Settings.Dump(settings.Add);File.WriteAllLines(Path.Combine(output,"effective-settings.txt"),settings);
                File.WriteAllText(Path.Combine(output,"biome-context.json"),ReferenceJson.Serialize(BiomeContextReference.Capture(world,wg)));
                var points=new List<Vector2>();var bins=new HashSet<string>();
                // Coarse discovery selects actual material/forest ranges, not just biome labels.
                for(int row=0;row<64;row++){
                    if(WorldGenerator.instance!=wg)throw new InvalidOperationException("World changed.");
                    for(int col=0;col<64;col++){
                        float x=((col+.5f)/64-.5f)*BC.TotalSize,z=(.5f-(row+.5f)/64)*BC.TotalSize;
                        var biome=wg.GetBiome(x,z);float h=wg.GetBiomeHeight(biome,x,z,out var mask,false,true);
                        float forest=WorldGenerator.GetForestFactor(new Vector3(x,0,z));
                        int forestBin=forest<.8f?0:forest<1.15f?1:forest<1.3f?2:3;
                        int alphaBin=mask.a<=0?0:mask.a<.5f?1:mask.a<1?2:3;
                        string key=(int)biome+":"+(h<30)+":"+forestBin+":"+alphaBin;
                        if(bins.Add(key))points.Add(new Vector2(x,z));
                    }
                    if(row%4==0)yield return null;
                }
                foreach(var sector in world.m_biomeData.Sectors.Where(s=>s.AltBiomes.Count>0))points.Add(sector.Center);
                // Native builder and selective copy must agree for masks as well as heights.
                var full=HeightmapBuilder.instance.RequestTerrainSync(Vector3.zero,64,1,false,wg);
                int verified=SelectiveTerrainBuilder.Verify(HeightmapBuilder.instance,wg,new Vector2s(0,0),full);
                var observations=new List<object>();
                foreach(var p in points.Distinct()){
                    if(WorldGenerator.instance!=wg)throw new InvalidOperationException("World changed.");
                    float x=p.x,z=p.y;var zone=ZoneSystem.GetZone(new Vector3(x,0,z));
                    int col=Math.Min(63,(int)Math.Floor(x-zone.x*64+32)),row=Math.Min(63,(int)Math.Floor(z-zone.y*64+32));
                    var data=SelectiveTerrainBuilder.Request(HeightmapBuilder.instance,wg,zone,col,row);
                    var nodes=new List<object>();
                    foreach(int iy in new[]{row,row+1})foreach(int ix in new[]{col,col+1})nodes.Add(new{x=zone.x*64-32+ix,z=zone.y*64-32+iy,mask=Mask(data.m_baseMask[iy*65+ix])});
                    var biome=wg.GetBiome(x,z);wg.GetBiomeHeight(biome,x,z,out var direct,false,true);
                    var sector=wg.GetBiomeSector(x,z);var color=Heightmap.GetBiomeColor(sector);
                    observations.Add(new{x=F(x),z=F(z),biome=(int)biome,forest=F(WorldGenerator.GetForestFactor(new Vector3(x,0,z))),directMask=Mask(direct),altNames=sector.AltBiomes.Select(a=>a.m_name).ToArray(),textureOverrides=sector.AltBiomes.Select(a=>(int)a.m_terrainTextureOverride).ToArray(),nativeTextureCode=new[]{(int)color.r,color.g,color.b,color.a},nodes});
                    yield return null;
                }
                var after=new List<string>();BC.Settings.Dump(after.Add);if(!settings.SequenceEqual(after))throw new InvalidOperationException("Settings changed.");
                File.WriteAllText(Path.Combine(output,"visual-points.json"),ReferenceJson.Serialize(new{format="sp-visual-points/1",observations}));
                File.WriteAllText(Path.Combine(output,"manifest.json"),ReferenceJson.Serialize(new{format="sp-visual-reference/1",status="complete-native-observations-not-offline-parity",preset="Continents",seed=world.m_seedName,numericSeed=world.m_seed,gameSha256=Hash(typeof(World).Assembly.Location),modSha256=Hash(typeof(BC).Assembly.Location),probeSha256=Hash(typeof(VisualReference).Assembly.Location),settingsSha256=Hash(Path.Combine(output,"effective-settings.txt")),contextSha256=Hash(Path.Combine(output,"biome-context.json")),pointsSha256=Hash(Path.Combine(output,"visual-points.json")),pointCount=observations.Count,selectiveVerifiedNodes=verified,plugins=BepInEx.Bootstrap.Chainloader.PluginInfos.Values.Select(v=>new{guid=v.Metadata.GUID,version=v.Metadata.Version.ToString()}).ToArray(),contract="Forest/direct mask at explicit XZ; four integer 1m nodes from original non-LOD builder. RGBA float bits; native sector texture codes are shader weights, not display RGB. No edits, waves or interpolated candidate reference."}));
                if(global::Console.instance!=null)global::Console.instance.Print("Visual reference captured: "+observations.Count+" points.");
            }finally{finished();}
        }
    }
}
