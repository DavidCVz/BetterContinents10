using System;
using System.Reflection;
using UnityEngine;

// Used only by explicit F03 bootstrap copies. Original Unity assemblies are never patched.
public static class NativeBridge
{
    static UnityIntegerRandomCandidate random=new UnityIntegerRandomCandidate(0);
    static readonly FieldInfo[] fields={typeof(UnityEngine.Random.State).GetField("s0",BindingFlags.Instance|BindingFlags.NonPublic),typeof(UnityEngine.Random.State).GetField("s1",BindingFlags.Instance|BindingFlags.NonPublic),typeof(UnityEngine.Random.State).GetField("s2",BindingFlags.Instance|BindingFlags.NonPublic),typeof(UnityEngine.Random.State).GetField("s3",BindingFlags.Instance|BindingFlags.NonPublic)};
    public static void InitState(int seed){random=new UnityIntegerRandomCandidate(seed);}
    public static int RangeInt(int a,int b)=>random.Range(a,b);
    public static float RangeFloat(float a,float b)=>random.Range(a,b);
    public static float Value()=>random.Value();
    public static float Perlin(float x,float y)=>UnityPerlinCandidate.Normalized(x,y,true,true);
    public static void GetState(out UnityEngine.Random.State state){object boxed=new UnityEngine.Random.State();var words=random.SaveState();for(int i=0;i<4;i++)fields[i].SetValue(boxed,unchecked((int)words[i]));state=(UnityEngine.Random.State)boxed;}
    public static void SetState(ref UnityEngine.Random.State state){object boxed=state;var words=new uint[4];for(int i=0;i<4;i++)words[i]=unchecked((uint)(int)fields[i].GetValue(boxed));random.RestoreState(words);}
}
