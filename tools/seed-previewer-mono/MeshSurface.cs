using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;

static class MeshSurface
{
    public static void CheckGeometry()
    {
        // A non-coplanar cell distinguishes both triangles from bilinear interpolation.
        var grid=new float[4225];grid[0]=10;grid[1]=20;grid[65]=30;grid[66]=70;
        if(Sample(grid,.25f,.25f)!=17.5f||Sample(grid,.75f,.75f)!=47.5f||Sample(grid,.25f,.75f)!=27.5f)throw new InvalidOperationException("Mesh triangle geometry check failed.");
        grid[4224]=123;if(Sample(grid,64,64)!=123)throw new InvalidOperationException("Mesh edge check failed.");
        bool rejected=false;try{Sample(grid,-.1f,0);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new InvalidOperationException("Invalid mesh coordinate accepted.");
    }
    // Collision topology audited in Heightmap.RebuildCollisionMesh: SW,NW,SE;
    // SE,NW,NE. No bilinear interpolation or continuous biome-height shortcut.
    public static float Sample(IList<float> heights,float x,float z)
    {
        if(heights.Count!=4225||float.IsNaN(x)||float.IsNaN(z)||x<0||z<0||x>64||z>64)throw new InvalidDataException("Invalid mesh sample.");
        int col=Math.Min(63,(int)Math.Floor(x)),row=Math.Min(63,(int)Math.Floor(z));
        double u=(double)x-col,v=(double)z-row;int i=row*65+col;
        double sw=heights[i],se=heights[i+1],nw=heights[i+65],ne=heights[i+66];
        return Triangle((float)sw,(float)se,(float)nw,(float)ne,u,v);
    }
    public static float Triangle(float sw,float se,float nw,float ne,double u,double v)=>
        (float)(u+v<=1?(double)sw+u*((double)se-sw)+v*((double)nw-sw):(double)ne+(1-u)*((double)nw-ne)+(1-v)*((double)se-ne));
    public static void VerifyTopology(string file)
    {
        using(var reader=new BinaryReader(File.OpenRead(file))){
            if(reader.BaseStream.Length!=64*64*6*4)throw new InvalidDataException("Unexpected topology size.");
            for(int row=0;row<64;row++)for(int col=0;col<64;col++){
                int i=row*65+col;foreach(int expected in new[]{i,i+65,i+1,i+1,i+65,i+66})if(reader.ReadInt32()!=expected)throw new InvalidDataException("Captured collision topology differs from the sampler.");
            }
        }
    }
    public static bool Compare(string reference,string output,Dictionary<string,HeightmapBuilder.HMBuildData> grids,WorldGenerator generator)
    {
        VerifyTopology(Path.Combine(reference,"mesh-triangles.i32"));
        var context=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(reference,"biome-context.json")));
        if((bool)context["water"]["hasBetterContinentsLakes"])throw new NotSupportedException("Lake water classification is not implemented.");
        float water=(float)context["water"]["baselineMetres"]["value"];
        if(water!=30f||(string)context["water"]["baselineMetres"]["bits"]!="41F00000"||(string)context["water"]["prefabWater"]?["worldY"]?["bits"]!="41F00000")throw new InvalidDataException("Expected verified 30 m ocean plane.");
        int count=0,biomeDifferences=0,waterDifferences=0,ambiguous=0;double max=0,sum=0;var seen=new HashSet<string>();
        using(var writer=new StreamWriter(Path.Combine(output,"fractional-comparison.tsv"))){
            writer.WriteLine("zoneX\tzoneZ\tx\tz\tsurfaceMetres\terrorMetres");
            foreach(string line in File.ReadLines(Path.Combine(reference,"fractional-points.tsv")).Skip(1)){
                var c=line.Split('\t');if(c.Length!=7||!seen.Add(string.Join(",",c.Take(4))))throw new InvalidDataException("Invalid or duplicate fractional point.");
                string key=c[0]+","+c[1];if(!grids.ContainsKey(key))throw new InvalidDataException("Unexpected fractional zone.");
                float x=float.Parse(c[2],CultureInfo.InvariantCulture),z=float.Parse(c[3],CultureInfo.InvariantCulture),expected=float.Parse(c[5],CultureInfo.InvariantCulture);
                if(float.IsNaN(expected)||float.IsInfinity(expected))throw new InvalidDataException("Non-finite fractional reference.");
                float surface=Sample(grids[key].m_baseHeights,x-int.Parse(c[0])*64+32,z-int.Parse(c[1])*64+32);
                int below=int.Parse(c[6]);if(below!=(expected<water?1:0))throw new InvalidDataException("Inconsistent static ocean reference.");
                if((int)generator.GetBiome(x,z)!=int.Parse(c[4]))biomeDifferences++;
                double error=Math.Abs((double)surface-expected);max=Math.Max(max,error);sum+=error;
                if(Math.Abs((double)expected-water)<=.1)ambiguous++;else if((surface<water?1:0)!=below)waterDifferences++;
                writer.WriteLine(string.Join("\t",c[0],c[1],c[2],c[3],surface.ToString("R",CultureInfo.InvariantCulture),error.ToString("R",CultureInfo.InvariantCulture)));count++;
            }
        }
        if(count!=61440)throw new InvalidDataException("Incomplete fractional reference.");
        File.WriteAllText(Path.Combine(output,"fractional-comparison.json"),SeedPreviewerReference.ReferenceJson.Serialize(new{count,biomeDifferences,maxSurfaceErrorMetres=max,meanSurfaceErrorMetres=sum/count,waterDifferences,ambiguous,status="fractional-pilot-not-global-map-acceptance"}));
        return biomeDifferences==0&&max<=.1&&waterDifferences==0;
    }
    public static void ExportPatches(string output,Dictionary<string,HeightmapBuilder.HMBuildData> grids,WorldGenerator generator)
    {
        if(BetterContinents.BetterContinents.Settings.HasLakes)throw new NotSupportedException("Lake masks are outside the ocean-plane patch exporter.");
        foreach(var entry in grids){
            var zone=entry.Key.Split(',');int zx=int.Parse(zone[0]),zz=int.Parse(zone[1]);string folder=Path.Combine(output,"patch-"+zx+"-"+zz);Directory.CreateDirectory(folder);
            using(var surface=new BinaryWriter(File.Create(Path.Combine(folder,"surface.f32"))))
            using(var biome=new BinaryWriter(File.Create(Path.Combine(folder,"biomes.i32"))))
            using(var water=new BinaryWriter(File.Create(Path.Combine(folder,"below-ocean.u8")))){
                for(int row=0;row<128;row++)for(int col=0;col<128;col++){
                    float localX=(col+.5f)*.5f,localZ=64-(row+.5f)*.5f;
                    float height=Sample(entry.Value.m_baseHeights,localX,localZ);
                    surface.Write(height);biome.Write((int)generator.GetBiome(zx*64-32+localX,zz*64-32+localZ));water.Write((byte)(height<30?1:0));
                }
            }
            File.WriteAllText(Path.Combine(folder,"layers.json"),SeedPreviewerReference.ReferenceJson.Serialize(new{size=128,zoneX=zx,zoneZ=zz,metresPerPixel=.5,orientation="north-first pixel centers, +X columns, -Z rows",surface="little-endian float32 metres; unedited triangle mesh",biomes="little-endian Int32 GetBiome flags",water="uint8: 1 below static ocean 30m; no volume or waves",status="candidate-local-patch-layers-not-global-acceptance"}));
        }
    }
}
