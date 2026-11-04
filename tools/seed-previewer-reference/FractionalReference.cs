using System;
using System.IO;
using System.Reflection;
using System.Globalization;
using UnityEngine;

namespace SeedPreviewerReference
{
    // Independent reference: actual loaded mesh indices and Unity triangle planes.
    // No offline interpolation formula is shared with the capturing plugin.
    internal static class FractionalReference
    {
        public static int[] CaptureTopology(string output)
        {
            var terrain=Heightmap.FindHeightmap(Player.m_localPlayer.transform.position);
            if(terrain==null)throw new InvalidOperationException("No loaded player heightmap for topology capture.");
            var mesh=(Mesh)typeof(Heightmap).GetField("m_collisionMesh",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(terrain);
            if(mesh==null||mesh.vertexCount!=4225)throw new InvalidDataException("Expected non-LOD 65x65 collision mesh.");
            var indices=mesh.triangles;
            if(indices.Length!=64*64*6)throw new InvalidDataException("Unexpected mesh triangle count.");
            using(var writer=new BinaryWriter(File.Create(Path.Combine(output,"mesh-triangles.i32"))))foreach(int i in indices)writer.Write(i);
            return indices;
        }
        public static void WriteZone(StreamWriter writer,int[] indices,Vector2s zone,HeightmapBuilder.HMBuildData data,WorldGenerator generator,float water)
        {
            var fractions=new[]{new Vector2(.125f,.25f),new Vector2(.75f,.875f),new Vector2(.25f,.75f),new Vector2(.375f,.375f),new Vector2(.875f,.125f)};
            for(int row=0;row<64;row++)for(int col=0;col<64;col++)foreach(var fraction in fractions){
                float x=zone.x*64-32+col+fraction.x,z=zone.y*64-32+row+fraction.y;
                var ray=new Ray(new Vector3(x,4096f,z),Vector3.down);bool found=false;float height=0;
                for(int triangle=0;triangle<2;triangle++){
                    int k=(row*64+col)*6+triangle*3;
                    var a=Vertex(indices[k],zone,data);var b=Vertex(indices[k+1],zone,data);var c=Vertex(indices[k+2],zone,data);
                    // Select the triangle by signed projected edges, independently of its diagonal.
                    if(!Inside(a,b,c,x,z))continue;
                    if(!new Plane(a,b,c).Raycast(ray,out float distance))throw new InvalidDataException("Triangle plane ray missed.");
                    height=ray.GetPoint(distance).y;found=true;break;
                }
                if(!found)throw new InvalidDataException("No containing mesh triangle.");
                writer.WriteLine(string.Join("\t",zone.x,zone.y,x.ToString("R",CultureInfo.InvariantCulture),z.ToString("R",CultureInfo.InvariantCulture),(int)generator.GetBiome(x,z),height.ToString("R",CultureInfo.InvariantCulture),height<water?1:0));
            }
        }
        public static float Surface(int[] indices,Vector2s zone,HeightmapBuilder.HMBuildData data,float x,float z)
        {
            int col=Math.Min(63,(int)Math.Floor(x-zone.x*64+32)),row=Math.Min(63,(int)Math.Floor(z-zone.y*64+32));
            if(col<0||row<0)throw new InvalidDataException("Reference point outside mesh.");
            var ray=new Ray(new Vector3(x,4096,z),Vector3.down);
            for(int triangle=0;triangle<2;triangle++){
                int k=(row*64+col)*6+triangle*3;var a=Vertex(indices[k],zone,data);var b=Vertex(indices[k+1],zone,data);var c=Vertex(indices[k+2],zone,data);
                if(Inside(a,b,c,x,z)&&new Plane(a,b,c).Raycast(ray,out float distance))return ray.GetPoint(distance).y;
            }
            throw new InvalidDataException("No triangle plane at reference point.");
        }
        static Vector3 Vertex(int index,Vector2s zone,HeightmapBuilder.HMBuildData data)=>new Vector3(zone.x*64-32+index%65,data.m_baseHeights[index],zone.y*64-32+index/65);
        static float Edge(Vector3 a,Vector3 b,float x,float z)=>(b.x-a.x)*(z-a.z)-(b.z-a.z)*(x-a.x);
        static bool Inside(Vector3 a,Vector3 b,Vector3 c,float x,float z){float p=Edge(a,b,x,z),q=Edge(b,c,x,z),r=Edge(c,a,x,z);return (p>=0&&q>=0&&r>=0)||(p<=0&&q<=0&&r<=0);}
    }
}
