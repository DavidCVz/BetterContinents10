using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SeedPreviewerReference
{
    // Independent engine reference. Never uses the offline candidates.
    internal static class NativeReference
    {
        static object F(float value)=>SeedPreviewerDiagnostics.PrecisionTrace.Float(value);
        static object State()
        {
            object state=UnityEngine.Random.state;
            var fields=state.GetType().GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).OrderBy(f=>f.Name).ToArray();
            if(fields.Length!=4)throw new NotSupportedException("Unexpected engine RNG state shape.");
            return fields.ToDictionary(f=>f.Name,f=>{
                object value=f.GetValue(state);
                uint bits=value is int ? unchecked((uint)(int)value) : (uint)value;
                return bits.ToString("X8");
            });
        }
        public static object Capture()
        {
            float[] axes={-1000.25f,-257f,-256f,-255.999f,-87.5f,-1.0001f,-1f,-0.999f,-0.5f,-0.125f,0f,0.00001f,0.125f,0.25f,0.5f,0.75f,0.999f,1f,1.0001f,53.875f,87.5f,255.999f,256f,257f,400.25f};
            var perlin=new List<object>();
            foreach(float x in axes)foreach(float y in axes)perlin.Add(new{x=F(x),y=F(y),value=F(Mathf.PerlinNoise(x,y))});
            var random=new List<object>();var original=UnityEngine.Random.state;
            try {
                foreach(int seed in new[]{874404622,874370830,30859693,0,-1,int.MinValue,int.MaxValue,1234}){
                    UnityEngine.Random.InitState(seed);var initial=State();var steps=new List<object>();
                    for(int operation=0;operation<8;operation++)for(int index=0;index<16;index++){
                        int? integer=null;object number=null;
                        switch(operation){
                            case 0:integer=UnityEngine.Random.Range(-10000,10000);break;
                            case 1:integer=UnityEngine.Random.Range(int.MinValue,int.MaxValue);break;
                            case 2:number=F(UnityEngine.Random.Range(-1f,1f));break;
                            case 3:number=F(UnityEngine.Random.value);break;
                            case 4:integer=UnityEngine.Random.Range(0,1);break;
                            case 5:integer=UnityEngine.Random.Range(-7,3);break;
                            case 6:integer=UnityEngine.Random.Range(5,5);break;
                            case 7:integer=UnityEngine.Random.Range(9,-4);break;
                        }
                        steps.Add(new{operation,index,integer,number,stateAfter=State()});
                    }
                    random.Add(new{seed,initial,steps});
                }
            } finally {UnityEngine.Random.state=original;}
            var water=typeof(ZoneSystem).GetField("m_waterLevel",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            if(water==null || water.FieldType!=typeof(float))throw new MissingFieldException("ZoneSystem.m_waterLevel");
            return new{
                format="sp-native-reference/1",perlin,random,
                operations=new[]{"Range(int:-10000,10000)","Range(int:min,max)","Range(float:-1,1)","value","Range(int:0,1)","Range(int:-7,3)","Range(int:5,5)","Range(int:9,-4)"},
                water=new{zoneSystemWaterLevelMetres=F((float)water.GetValue(ZoneSystem.instance)),limitation="Ocean baseline only; no per-point volume coverage or land/water acceptance."},
                stateRestored=true,limitation="Directed primitive samples, not proof of full-domain equivalence. No RNG yield or terrain modification."
            };
        }
    }
}
