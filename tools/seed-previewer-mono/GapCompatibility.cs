using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

static class GapCompatibility
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    sealed class Target { [MethodImpl(MethodImplOptions.NoInlining)] public double Gap(float x,float y)=>x*0.125+y*0.25; }
    public static bool Prefix(ref double __result){__result=1;return false;}
    public static int Run(string repo,string bootstrap,string output,string library)
    {
        repo=Path.GetFullPath(repo);bootstrap=Path.GetFullPath(bootstrap);
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Output must be new.");Directory.CreateDirectory(output);
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            foreach(var dir in new[]{bootstrap,Path.Combine(repo,"tools/seed-previewer/.local/libs"),Path.Combine(repo,"bin/Release/net4.8")}){
                var path=Path.Combine(dir,new AssemblyName(e.Name).Name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path);
            }return null;
        };
        var pathHarmony=library=="HarmonyX"?Path.Combine(repo,"tools/seed-previewer/.local/libs/0Harmony.dll"):Path.Combine(repo,"tools/seed-previewer/.local/packages/lib.harmony/2.3.6/lib/net48/0Harmony.dll");
        var assembly=Assembly.LoadFrom(pathHarmony);
        return Check(repo,output,assembly);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static int Check(string repo,string output,Assembly assembly)
    {
        var harmonyType=assembly.GetType("HarmonyLib.Harmony",true);var hmType=assembly.GetType("HarmonyLib.HarmonyMethod",true);
        var harmony=Activator.CreateInstance(harmonyType,new object[]{"seedpreviewer.gap.compatibility"});
        var patch=harmonyType.GetMethods().Where(m=>m.Name=="Patch"&&m.GetParameters().Length>=5&&m.GetParameters()[0].ParameterType==typeof(MethodBase)).OrderByDescending(m=>m.GetParameters().Length).First();
        var prefix=Activator.CreateInstance(hmType,new object[]{typeof(GapCompatibility).GetMethod("Prefix")});
        var targets=new object[]{new Target(),FormatterServices.GetUninitializedObject(typeof(WorldGenerator))};
        typeof(WorldGenerator).GetField("m_world",All).SetValue(targets[1],new World());
        var results=new System.Collections.Generic.List<object>();
        for(int i=0;i<2;i++){
            var target=targets[i];var method=target.GetType().GetMethod(i==0?"Gap":"CreateAshlandsGap",All);
            var call=(Func<float,float,double>)Delegate.CreateDelegate(typeof(Func<float,float,double>),target,method);
            double baseline=call(5000,-8000);
            File.AppendAllText(Path.Combine(output,"stages.log"),"baseline "+method.Name+"="+baseline+Environment.NewLine);
            var args=new object[patch.GetParameters().Length];args[0]=method;args[1]=prefix;patch.Invoke(harmony,args);
            File.AppendAllText(Path.Combine(output,"stages.log"),"patched "+method.Name+Environment.NewLine);
            double patched=call(5000,-8000);
            if(patched!=1)throw new Exception("Prefix result mismatch.");
            harmonyType.GetMethod("Unpatch",new[]{typeof(MethodBase),typeof(MethodInfo)}).Invoke(harmony,new object[]{method,typeof(GapCompatibility).GetMethod("Prefix")});
            double restored=call(5000,-8000);if(restored!=baseline)throw new Exception("Unpatch mismatch.");
            results.Add(new {method=method.Name,baseline,patched,restored});
        }
        // Serialize without loading the mod; only diagnostic numeric values/names are emitted.
        File.WriteAllText(Path.Combine(output,"success.txt"),"Both instance-double targets patched to 1 and restored. Harmony="+assembly.GetName().Version);
        System.Console.WriteLine("PASS isolated instance-double patch/unpatch: "+assembly.GetName().Version);
        return 0;
    }
}
