using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using UnityEngine;
using BC=BetterContinents.BetterContinents;

namespace SeedPreviewerReference
{
    // Independent native builder reference. Never uses SparseSurface or its formulas.
    internal static class GlobalReference
    {
        sealed class Point {public float X,Z,H;public int Biome;}
        static string Hash(string file){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","");}
        static void Print(string message){if(global::Console.instance!=null)global::Console.instance.Print(message);}
        static string Settings(){var lines=new List<string>();BC.Settings.Dump(lines.Add);return string.Join("\n",lines);}
        public static IEnumerator Capture(string preset,Func<bool> cancelled,Action finished,bool selective=true)
        {
            string output=Path.Combine(Paths.BepInExRootPath,"seed-previewer-global-reference",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            try{
                var elapsed=System.Diagnostics.Stopwatch.StartNew();
                var generator=WorldGenerator.instance;var world=generator.m_world;
                if(!new[]{"AbC123","abc123","North2026"}.Contains(world.m_seedName)||BC.Settings.HasLakes)throw new NotSupportedException("F04 supports the three planned seeds and static ocean only.");
                var prefab=ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>();
                var volume=ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<WaterVolume>();
                if(prefab.m_width!=64||prefab.m_scale!=1||volume==null||volume.transform.position.y!=30||ZoneSystem.instance.m_waterLevel!=30)throw new NotSupportedException("Unexpected terrain or water geometry.");
                string settings=Settings();File.WriteAllText(Path.Combine(output,"effective-settings.txt"),settings);
                File.WriteAllText(Path.Combine(output,"biome-context.json"),ReferenceJson.Serialize(BiomeContextReference.Capture(world,generator)));
                var indices=FractionalReference.CaptureTopology(output);float span=BC.TotalSize;const int size=256;
                int selectiveChecks=0;
                if(selective){
                    SelectiveTerrainBuilder.Prepare();
                    foreach(var zone in new[]{new Vector2s(0,0),new Vector2s(16,-7),new Vector2s(-83,59),new Vector2s(140,70),new Vector2s(-140,-70),new Vector2s(0,-150),new Vector2s(0,150)}){
                        var full=HeightmapBuilder.instance.RequestTerrainSync(ZoneSystem.GetZonePos(zone),64,1,false,generator);
                        selectiveChecks+=SelectiveTerrainBuilder.Verify(HeightmapBuilder.instance,generator,zone,full);
                    }
                    Print("Selective native builder preflight passed: "+selectiveChecks+" exact node checks.");
                }
                var points=new Point[size*size];var cache=new Dictionary<string,HeightmapBuilder.HMBuildData>();
                Func<float,float,Point> sample=(x,z)=>{
                    if(WorldGenerator.instance!=generator||Settings()!=settings)throw new InvalidOperationException("World/settings changed during global capture.");
                    var zone=ZoneSystem.GetZone(new Vector3(x,0,z));int column=Math.Min(63,(int)Math.Floor(x-zone.x*64+32)),line=Math.Min(63,(int)Math.Floor(z-zone.y*64+32));
                    string key=zone.x+","+zone.y+(selective?","+column+","+line:"");
                    if(!cache.TryGetValue(key,out var data)){
                        if(cache.Count>=32)cache.Clear();
                        var state=UnityEngine.Random.state;
                        try{data=selective?SelectiveTerrainBuilder.Request(HeightmapBuilder.instance,generator,zone,column,line):HeightmapBuilder.instance.RequestTerrainSync(ZoneSystem.GetZonePos(zone),64,1,false,generator);}finally{UnityEngine.Random.state=state;}
                        cache.Add(key,data);
                    }
                    float height=FractionalReference.Surface(indices,zone,data,x,z);
                    if(float.IsNaN(height)||float.IsInfinity(height))throw new InvalidDataException("Non-finite reference height.");
                    return new Point{X=x,Z=z,H=height,Biome=(int)generator.GetBiome(x,z)};
                };
                int count=0;var seen=new HashSet<string>();
                using(var writer=new StreamWriter(Path.Combine(output,"points.tsv"))){
                    writer.WriteLine("kind\trow\tcol\tx\tz\tbiome\tsurfaceMetres\tbelowOceanPlane");
                    Action<string,int,int,Point> write=(kind,row,col,p)=>{
                        string key=p.X.ToString("R",CultureInfo.InvariantCulture)+","+p.Z.ToString("R",CultureInfo.InvariantCulture);
                        if(!seen.Add(key))throw new InvalidDataException("Duplicate reference point.");
                        writer.WriteLine(string.Join("\t",kind,row,col,p.X.ToString("R",CultureInfo.InvariantCulture),p.Z.ToString("R",CultureInfo.InvariantCulture),p.Biome,p.H.ToString("R",CultureInfo.InvariantCulture),p.H<30?1:0));count++;
                    };
                    Print("Global reference started: 256x256; sp_reference_cancel to cancel.");
                    var frame=System.Diagnostics.Stopwatch.StartNew();int budget=selective?64:8;
                    for(int row=0;row<size;row++){
                        for(int col=0;col<size;col++){
                            var p=sample(((col+.5f)/size-.5f)*span,(.5f-(row+.5f)/size)*span);points[row*size+col]=p;write("grid",row,col,p);
                            if(col%budget==budget-1||frame.ElapsedMilliseconds>=8){if(cancelled()){Print("Global reference cancelled; no complete manifest.");yield break;}yield return null;frame.Restart();}
                        }
                        writer.Flush();Print("Global reference rows: "+(row+1)+"/256");
                    }
                    var coasts=new List<Tuple<Point,Point>>();var transitions=new List<Tuple<Point,Point>>();
                    for(int row=0;row<size;row++)for(int col=0;col<size;col++)foreach(int next in new[]{col+1<size?row*size+col+1:-1,row+1<size?(row+1)*size+col:-1}){
                        if(next<0)continue;var a=points[row*size+col];var b=points[next];
                        if((a.H<30)!=(b.H<30))coasts.Add(Tuple.Create(a,b));if(a.Biome!=b.Biome)transitions.Add(Tuple.Create(a,b));
                    }
                    if(coasts.Count<50||transitions.Count<50)throw new InvalidDataException("Insufficient coast/transition reference candidates.");
                    foreach(string kind in new[]{"coast","transition"}){
                        var candidates=kind=="coast"?coasts:transitions;
                        for(int i=0;i<50;i++){
                            var pair=candidates[(int)((long)i*candidates.Count/50)];var a=pair.Item1;var b=pair.Item2;
                            for(int step=0;step<12;step++){
                                var mid=sample((float)(((double)a.X+b.X)/2),(float)(((double)a.Z+b.Z)/2));
                                if(kind=="coast"?(mid.H<30)==(a.H<30):mid.Biome==a.Biome)a=mid;else b=mid;
                            }
                            // Midpoint after refinement avoids duplicate grid endpoints.
                            var p=sample((float)(((double)a.X+b.X)/2),(float)(((double)a.Z+b.Z)/2));write(kind,-1,-1,p);
                            if(cancelled()){Print("Global reference cancelled; no complete manifest.");yield break;}yield return null;
                        }
                    }
                    foreach(float x in new[]{-span/2,0,span/2})foreach(float z in new[]{-span/2,0,span/2})write("anchor",-1,-1,sample(x,z));
                }
                if(Settings()!=settings||WorldGenerator.instance!=generator)throw new InvalidOperationException("World/settings changed at completion.");
                int worldVersion=(int)typeof(World).GetField("m_worldGenVersion",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(world);
                File.WriteAllText(Path.Combine(output,"manifest.json"),ReferenceJson.Serialize(new{
                    format="sp-reference-global/1",captureVersion="0.2.1",contract="unedited-nonlod-mesh-ocean30/1",preset,seed=world.m_seedName,numericSeed=world.m_seed,worldVersion,size,totalSizeMetres=span,count,
                    captureMode=selective?"selective-original-body":"full-original-body",selectiveChecks,elapsedMs=elapsed.ElapsedMilliseconds,
                    gameSha256=Hash(typeof(WorldGenerator).Assembly.Location),modSha256=Hash(typeof(BC).Assembly.Location),
                    settingsSha256=Hash(Path.Combine(output,"effective-settings.txt")),pointsSha256=Hash(Path.Combine(output,"points.tsv")),biomeContextSha256=Hash(Path.Combine(output,"biome-context.json")),topologySha256=Hash(Path.Combine(output,"mesh-triangles.i32")),
                    coastCount=50,transitionCount=50,anchorCount=9,
                    plugins=BepInEx.Bootstrap.Chainloader.PluginInfos.Values.Select(p=>new{guid=p.Metadata.GUID,version=p.Metadata.Version.ToString()}).ToArray(),
                    limitation="Native builder grids and Unity triangle planes, not PhysX. Static ocean only; excludes scene volumes, waves and player edits."
                }));
                Print("Global reference captured (256x256 + 109 extra points).");
            }finally{finished();}
        }
    }
}
