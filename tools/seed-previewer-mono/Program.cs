using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Globalization;
using System.Security.Cryptography;
using BepInEx.Configuration;
using UnityEngine;
using BC=BetterContinents.BetterContinents;

// Experimental standalone compatibility probe. Never deploy adapted assemblies into the game.
static class Entry
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int Main(string[] args)
    {
        try {
            if(args.Length==3 && args[0]=="--verify-native")return NativeCandidateVerification.Run(args[1],args[2]);
            if(args.Length!=5 && args.Length!=6 && args.Length!=7) throw new ArgumentException("Expected repo, bootstrap directory, preset name, seed, new output directory, optional pilot directory.");
            args[0]=Path.GetFullPath(args[0]); args[1]=Path.GetFullPath(args[1]); args[4]=Path.GetFullPath(args[4]);
            if(!new[]{"Continents","Pangaea","Swirls","Vanillaish"}.Contains(args[2]) || args[3].Length<1 || args[3].Length>10 || !args[3].All(c=>c<128&&char.IsLetterOrDigit(c))) throw new ArgumentException("Unsupported preset or seed.");
            if(Directory.Exists(args[4]) || File.Exists(args[4])) throw new IOException("Output must be new.");
            var libs=Path.Combine(args[0],"tools/seed-previewer/.local/libs");
            AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
                var name=new AssemblyName(e.Name).Name+".dll";
                foreach(var dir in new[]{args[1],Path.Combine(args[0],"tools/seed-previewer/.local/packages/lib.harmony/2.3.6/lib/net48"),libs,Path.Combine(args[0],"bin/Release/net4.8")}) {
                    var path=Path.Combine(dir,name); if(File.Exists(path)) return Assembly.LoadFrom(path);
                } return null;
            };
            if(args.Length==7 && (!int.TryParse(args[6],out int requestedSize)||requestedSize<64||requestedSize>2048))throw new ArgumentException("Resolution must be 64..2048.");
            Directory.CreateDirectory(args[4]);
            var bep=Assembly.LoadFrom(Path.Combine(libs,"BepInEx.dll")).GetType("BepInEx.Paths");
            bep.GetProperty("ConfigPath",All).SetValue(null,args[4],null);
            bep.GetProperty("BepInExConfigPath",All).SetValue(null,Path.Combine(args[4],"bepinex.cfg"),null);
            Assembly.LoadFrom(Path.Combine(args[1],"assembly_utils.dll"));
            Assembly.LoadFrom(Path.Combine(args[1],"BetterContinents.dll"));
            return Run(args);
        } catch(Exception e) { while(e.InnerException!=null)e=e.InnerException; System.Console.Error.WriteLine(e); return 1; }
    }
    static string Hash(string file){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(file))).Replace("-","");}
    static object Snapshot(BC.BetterContinentsSettings settings)=>settings.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance).Where(f=>f.FieldType==typeof(float)).ToDictionary(f=>f.Name,f=>Bits((float)f.GetValue(settings)));
    static string Bits(float value)=>BitConverter.ToInt32(BitConverter.GetBytes(value),0).ToString("X8");
    static float Height(float x,float z,float mountain=1000f){float result=0; if(BC.WorldGeneratorPatch.GetBaseHeightPrefixV3(ref x,ref z,ref result,mountain))throw new Exception("Unexpected vanilla delegation.");return result;}
    [MethodImpl(MethodImplOptions.NoInlining)] static int Run(string[] args)
    {
        UnityEngine.Debug.unityLogger.logHandler=new Log();
        var config=new ConfigFile(Path.Combine(args[4],"defaults.cfg"),false){SaveOnConfigSet=false};
        typeof(BC).GetMethod("DeclareConfig",All).Invoke(null,new object[]{config});
        var build=typeof(BC).Assembly.GetType("BetterContinents.NewWorldBuild"); var choiceType=build.GetNestedType("Choice",All); var choice=Activator.CreateInstance(choiceType,true);
        var preset=Path.Combine(args[0],"assets/BCAssets/Presets",args[2]+".BetterContinents");
        choiceType.GetField("PresetPath",All).SetValue(choice,preset); choiceType.GetField("WideSectors",All).SetValue(choice,BC.ConfigWideSectors.Value);
        var settings=(BC.BetterContinentsSettings)build.GetMethod("Build",All).Invoke(null,new[]{choice});
        var before=Snapshot(settings);int presetVersion=settings.Version;
        var pkg=new ZPackage();settings.Serialize(pkg,false);BC.Settings=BC.BetterContinentsSettings.Load(new ZPackage(pkg.GetArray()));
        File.WriteAllBytes(Path.Combine(args[4],"saved-settings.bin"),pkg.GetArray());
        File.WriteAllText(Path.Combine(args[4],"settings-lifecycle.json"),SeedPreviewerReference.ReferenceJson.Serialize(new{presetVersion,loadedVersion=BC.Settings.Version,beforeFloats=before,afterFloats=Snapshot(BC.Settings)}));
        var dump=new System.Collections.Generic.List<string>();BC.Settings.Dump(dump.Add);File.WriteAllLines(Path.Combine(args[4],"effective-settings.txt"),dump);
        typeof(BC).GetMethod("UpdateGeometry",All).Invoke(null,null);
        // Resolve seed conversion from its real declaring assembly.
        var seedType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("StringExtensionMethods")).FirstOrDefault(t=>t!=null);
        if(seedType==null)seedType=Assembly.LoadFrom(Path.Combine(args[0],"tools/seed-previewer/.local/libs/assembly_utils.dll")).GetType("StringExtensionMethods");
        int seed=(int)seedType.GetMethod("GetStableHashCode",All,null,new[]{typeof(string)},null).Invoke(null,new object[]{args[3]});
        var world=new World();world.m_seed=seed;
        typeof(BC.WorldGeneratorPatch).GetMethod("InitializePrefix",All).Invoke(null,new object[]{world});
        var setup=(WorldGenerator)FormatterServices.GetUninitializedObject(typeof(WorldGenerator));
        var mountainField=typeof(WorldGenerator).GetField("m_minMountainDistance",All);mountainField.SetValue(setup,1000f);
        typeof(WorldGenerator).GetMethod("VersionSetup",All).Invoke(setup,new object[]{2});
        if((float)mountainField.GetValue(setup)!=1000f)throw new Exception("Unexpected version setup.");
        File.WriteAllText(Path.Combine(args[4],"precision-trace.json"),SeedPreviewerReference.ReferenceJson.Serialize(SeedPreviewerDiagnostics.PrecisionTrace.Capture((x,z)=>Height(x,z))));
        BranchChecks(args[4]);
        int differences=0,points=0;
        if(args.Length>=6 && args[5]!="-"){
            if(args[2]!="Continents"||args[3]!="AbC123")throw new Exception("Pilot supports only Continents/AbC123.");
            using(var writer=new StreamWriter(Path.Combine(args[4],"pilot.tsv"))){
                writer.WriteLine("x\tz\tactual\tbits");
                foreach(var line in File.ReadAllLines(Path.Combine(args[5],"points.tsv")).Skip(1)){
                    var c=line.Split('\t');float x=float.Parse(c[2],CultureInfo.InvariantCulture),z=float.Parse(c[3],CultureInfo.InvariantCulture),expected=float.Parse(c[4],CultureInfo.InvariantCulture),actual=Height(x,z);
                    if(Bits(actual)!=Bits(expected))differences++;points++;
                    writer.WriteLine(c[2]+"\t"+c[3]+"\t"+actual.ToString("R",CultureInfo.InvariantCulture)+"\t"+Bits(actual));
                }
            }
            if(points!=12675)throw new Exception("Incomplete pilot.");
        }
        int size=args.Length==7?int.Parse(args[6],CultureInfo.InvariantCulture):1024;float total=BC.TotalSize,min=float.MaxValue,max=float.MinValue;
        var grid=Path.Combine(args[4],"height-base.f32");
        using(var writer=new BinaryWriter(File.Create(grid))) for(int row=0;row<size;row++)for(int col=0;col<size;col++){
            float x=((col+0.5f)/size-0.5f)*total,z=(0.5f-(row+0.5f)/size)*total,v=Height(x,z);
            if(float.IsNaN(v)||float.IsInfinity(v))throw new Exception("Non-finite grid.");writer.Write(v);min=Math.Min(min,v);max=Math.Max(max,v);
        }
        File.WriteAllText(Path.Combine(args[4],"generation.json"),SeedPreviewerReference.ReferenceJson.Serialize(new {status="experimental-full-V3-Mono-not-global-parity",preset=args[2],seed=args[3],numericSeed=seed,size,totalSizeMetres=total,min,max,settingsVersion=BC.Settings.Version,dataSha256=Hash(grid),modSha256=Hash(typeof(BC).Assembly.Location),utilsSha256=Hash(typeof(Utils).Assembly.Location),pilotPoints=points,pilotDifferences=differences}));
        System.Console.WriteLine("V3 complete; pilot="+points+" differences="+differences);return differences==0?0:2;
    }
    static void BranchChecks(string output){
        var s=BC.Settings;bool edge=s.DisableMapEdgeDropoff,center=s.MountainsAllowedAtCenter;float sea=s.SeaLevelAdjustment;
        try {
            s.SeaLevelAdjustment=sea+1f;s.MountainsAllowedAtCenter=true;s.DisableMapEdgeDropoff=true;
            var records=new System.Collections.Generic.List<object>();
            foreach(float x in new[]{0f,600f,800f,999f,1000f,1001f}){
                s.MountainsAllowedAtCenter=true;float plain=Height(x,0);s.MountainsAllowedAtCenter=false;float restricted=Height(x,0);
                if(x<1000f && plain>0.38f && !(restricted<plain))throw new Exception("Center restriction did not apply.");
                if(x>=1000f && Bits(plain)!=Bits(restricted))throw new Exception("Center restriction exceeded radius.");
                records.Add(new {kind="center",x,plain,restricted});
            }
            s.MountainsAllowedAtCenter=true;
            foreach(float x in new[]{10000f,10250f,10490f,10495f,10500f,10501f}){
                s.DisableMapEdgeDropoff=true;float plain=Height(x,0);s.DisableMapEdgeDropoff=false;float dropped=Height(x,0);
                if(x==10000f && Bits(plain)!=Bits(dropped))throw new Exception("Edge starts too early.");
                if(x>=10500f && dropped!=-2f)throw new Exception("Edge endpoint mismatch.");
                if(x>10000f && !(dropped<plain))throw new Exception("Edge drop did not apply.");
                records.Add(new {kind="edge",x,plain,dropped});
            }
            File.WriteAllText(Path.Combine(output,"branches.json"),SeedPreviewerReference.ReferenceJson.Serialize(records));
        } finally {s.DisableMapEdgeDropoff=edge;s.MountainsAllowedAtCenter=center;s.SeaLevelAdjustment=sea;}
    }
    class Log:ILogHandler { public void LogFormat(LogType t,UnityEngine.Object c,string f,params object[] a){System.Console.Error.WriteLine(string.Format(f,a));}public void LogException(Exception e,UnityEngine.Object c){System.Console.Error.WriteLine(e);} }
}
