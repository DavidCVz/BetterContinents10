using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using BC=BetterContinents.BetterContinents;

// Non-LOD HeightmapBuilder vertex evaluation, audited against the original IL.
// Biome functions and SmoothStep/Lerp are the actual game's managed methods.
sealed class SparseSurface
{
    readonly WorldGenerator generator;
    readonly int zx,zz;
    readonly Heightmap.Biome sw,se,nw,ne;
    public SparseSurface(WorldGenerator generator,int zx,int zz)
    {
        this.generator=generator;this.zx=zx;this.zz=zz;
        sw=generator.GetBiome(zx*64-32,zz*64-32);se=generator.GetBiome(zx*64+32,zz*64-32);
        nw=generator.GetBiome(zx*64-32,zz*64+32);ne=generator.GetBiome(zx*64+32,zz*64+32);
    }
    public float Vertex(int col,int row)
    {
        float x=zx*64-32+col,z=zz*64-32+row;
        float a=generator.GetBiomeHeight(sw,x,z,out _,false,true);
        if(sw==se&&sw==nw&&sw==ne)return a;
        float b=generator.GetBiomeHeight(se,x,z,out _,false,true),c=generator.GetBiomeHeight(nw,x,z,out _,false,true),d=generator.GetBiomeHeight(ne,x,z,out _,false,true);
        float u=DUtils.SmoothStep(0,1,(float)((double)col/64)),v=DUtils.SmoothStep(0,1,(float)((double)row/64));
        return DUtils.Lerp(DUtils.Lerp(a,b,u),DUtils.Lerp(c,d,u),v);
    }
    public float Sample(float x,float z)
    {
        float lx=x-zx*64+32,lz=z-zz*64+32;int col=Math.Min(63,(int)Math.Floor(lx)),row=Math.Min(63,(int)Math.Floor(lz));
        if(col<0||row<0||lx>64||lz>64)throw new InvalidDataException("Sparse sample outside zone.");
        return MeshSurface.Triangle(Vertex(col,row),Vertex(col+1,row),Vertex(col,row+1),Vertex(col+1,row+1),lx-col,lz-row);
    }
    public static void Verify(WorldGenerator generator,Dictionary<string,HeightmapBuilder.HMBuildData> grids,string output)
    {
        int points=0,differences=0;double max=0;
        foreach(var entry in grids){var zone=entry.Key.Split(',');var sampler=new SparseSurface(generator,int.Parse(zone[0]),int.Parse(zone[1]));
            for(int row=0;row<=64;row++)for(int col=0;col<=64;col++){
                float a=sampler.Vertex(col,row),b=entry.Value.m_baseHeights[row*65+col];
                if(BitConverter.ToInt32(BitConverter.GetBytes(a),0)!=BitConverter.ToInt32(BitConverter.GetBytes(b),0))differences++;
                max=Math.Max(max,Math.Abs((double)a-b));points++;
            }
        }
        File.WriteAllText(Path.Combine(output,"sparse-comparison.json"),SeedPreviewerReference.ReferenceJson.Serialize(new{points,differences,max}));
        if(differences!=0)throw new InvalidDataException("Sparse vertices differ from native HeightmapBuilder.");
    }
    public static int Generate(World world,string[] args)
    {
        var timer=Stopwatch.StartNew();var generator=TerrainPilot.Initialize(world,args);
        BiomeContext.InitializeAndCompare(world,generator,args[5],args[4],false);
        if(BC.Settings.HasLakes)throw new NotSupportedException("Only static ocean water is supported.");
        // Include modified sectors and distant transitions in the native-vs-sparse audit.
        var builder=(HeightmapBuilder)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(HeightmapBuilder));
        var build=typeof(HeightmapBuilder).GetMethod("Build",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        var grids=new Dictionary<string,HeightmapBuilder.HMBuildData>();
        var zones=new List<Vector2s>{new Vector2s(0,0),new Vector2s(16,-7),new Vector2s(-83,59)};
        zones.AddRange(world.m_biomeData.Sectors.Where(s=>s.AltBiomes.Count>0).Take(12).Select(s=>ZoneSystem.GetZone(new Vector3(s.Center.x,0,s.Center.y))));
        zones.AddRange(new[]{new Vector2s(140,70),new Vector2s(-140,-70),new Vector2s(0,-150),new Vector2s(0,150)});
        foreach(var zone in zones){string key=zone.x+","+zone.y;if(grids.ContainsKey(key))continue;var request=new HeightmapBuilder.HMBuildData(new Vector3(zone.x*64,0,zone.y*64),64,1,false,generator);build.Invoke(builder,new object[]{request});grids.Add(key,request);}
        Verify(generator,grids,args[4]);
        long initializationMs=timer.ElapsedMilliseconds;int size=int.Parse(args[6]);float span=BC.TotalSize;
        var heights=new float[size*size];var biomes=new int[size*size];var water=new byte[size*size];
        {
            int cachedZoneZ=int.MinValue;var cache=new Dictionary<int,SparseSurface>();
            for(int row=0;row<size;row++){
                float z=(.5f-(row+.5f)/size)*span;int zoneZ=(int)Math.Floor(((double)z+32)/64);
                if(zoneZ!=cachedZoneZ){cache.Clear();cachedZoneZ=zoneZ;}
                for(int col=0;col<size;col++){
                    float x=((col+.5f)/size-.5f)*span;int zoneX=(int)Math.Floor(((double)x+32)/64);
                    if(!cache.TryGetValue(zoneX,out var sampler)){sampler=new SparseSurface(generator,zoneX,zoneZ);cache.Add(zoneX,sampler);}
                    float height=sampler.Sample(x,z);if(float.IsNaN(height)||float.IsInfinity(height))throw new InvalidDataException("Non-finite surface.");
                    int index=row*size+col;heights[index]=height;biomes[index]=(int)generator.GetBiome(x,z);water[index]=(byte)(height<30?1:0);
                }
                if(row%128==0)System.Console.WriteLine("MAP row="+row+"/"+size);
            }
        }
        long samplingMs=timer.ElapsedMilliseconds-initializationMs;var export=Stopwatch.StartNew();
        using(var writer=new BinaryWriter(File.Create(Path.Combine(args[4],"surface.f32"))))foreach(float height in heights)writer.Write(height);
        using(var writer=new BinaryWriter(File.Create(Path.Combine(args[4],"biomes.i32"))))foreach(int biome in biomes)writer.Write(biome);
        File.WriteAllBytes(Path.Combine(args[4],"below-ocean.u8"),water);
        File.WriteAllText(Path.Combine(args[4],"map.json"),SeedPreviewerReference.ReferenceJson.Serialize(new{status="offline-map-not-global-game-parity",preset=args[2],seed=args[3],size,totalSizeMetres=span,orientation="north-first pixel centers",surface="unmodified non-LOD 1m triangle mesh; sparse vertex evaluation",water="below static 30m ocean plane; excludes volumes and waves",configurationMs=Entry.ConfigurationMs,initializationMs,samplingMs,binaryExportMs=export.ElapsedMilliseconds,sectorCount=world.m_biomeData.Sectors.Count,modifiedSectors=world.m_biomeData.Sectors.Count(s=>s.AltBiomes.Count>0)}));
        return 0;
    }
}
