using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SeedPreviewerReference
{
    // A PRIVATE reverse-patched copy of the installed Build method, not a patch
    // on the engine builder. Only row/column loop bounds change; formulas,
    // grid width/scale, coordinate arithmetic and corner lookup stay original.
    public static class SelectiveTerrainBuilder
    {
        static bool prepared;
        [ThreadStatic] static int row,col;
        static int StartRow()=>row;
        static int StartCol()=>col;
        static int EndRow()=>row+2;
        static int EndCol()=>col+2;
        public static void Prepare()
        {
            if(prepared)return;
            var original=typeof(HeightmapBuilder).GetMethod("Build",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            Harmony.ReversePatch(original,new HarmonyMethod(typeof(SelectiveTerrainBuilder).GetMethod(nameof(Build),BindingFlags.Static|BindingFlags.NonPublic)),typeof(SelectiveTerrainBuilder).GetMethod(nameof(SelectLoops),BindingFlags.Static|BindingFlags.NonPublic),null);
            prepared=true;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Build(HeightmapBuilder builder,HeightmapBuilder.HMBuildData data){throw new InvalidOperationException("Selective builder not prepared.");}
        static int Local(CodeInstruction instruction)
        {
            if(instruction.operand is LocalVariableInfo info)return info.LocalIndex;
            if(instruction.operand is int i)return i;
            if(instruction.operand is byte b)return b;
            if(instruction.opcode==OpCodes.Ldloc_0)return 0;
            return -1;
        }
        static IEnumerable<CodeInstruction> SelectLoops(IEnumerable<CodeInstruction> source)
        {
            var code=source.ToList();int starts=0,ends=0;
            for(int i=0;i<code.Count-2;i++)foreach(int local in new[]{11,14}){
                if(code[i].opcode==OpCodes.Ldc_I4_0&&(code[i+1].opcode==OpCodes.Stloc_S||code[i+1].opcode==OpCodes.Stloc)&&Local(code[i+1])==local){
                    code[i].opcode=OpCodes.Call;code[i].operand=typeof(SelectiveTerrainBuilder).GetMethod(local==11?nameof(StartRow):nameof(StartCol),BindingFlags.Static|BindingFlags.NonPublic);starts++;
                }
                if((code[i].opcode==OpCodes.Ldloc_S||code[i].opcode==OpCodes.Ldloc)&&Local(code[i])==local&&code[i+1].opcode==OpCodes.Ldloc_0&&(code[i+2].opcode==OpCodes.Blt||code[i+2].opcode==OpCodes.Blt_S)){
                    code[i+1].opcode=OpCodes.Call;code[i+1].operand=typeof(SelectiveTerrainBuilder).GetMethod(local==11?nameof(EndRow):nameof(EndCol),BindingFlags.Static|BindingFlags.NonPublic);ends++;
                }
            }
            if(starts!=2||ends!=2)throw new InvalidDataException("Native builder loop shape changed; selective capture disabled.");
            return code;
        }
        public static HeightmapBuilder.HMBuildData Request(HeightmapBuilder builder,WorldGenerator generator,Vector2s zone,int column,int line)
        {
            if(column<0||column>63||line<0||line>63)throw new ArgumentOutOfRangeException("cell");
            Prepare();row=line;col=column;
            var data=new HeightmapBuilder.HMBuildData(new Vector3(zone.x*64,0,zone.y*64),64,1,false,generator);
            Build(builder,data);return data;
        }
        public static int Verify(HeightmapBuilder builder,WorldGenerator generator,Vector2s zone,HeightmapBuilder.HMBuildData full)
        {
            int count=0;
            foreach(int y in new[]{0,1,31,32,63})foreach(int x in new[]{0,1,31,32,63}){
                var selected=Request(builder,generator,zone,x,y);
                foreach(int index in new[]{y*65+x,y*65+x+1,(y+1)*65+x,(y+1)*65+x+1}){
                    if(BitConverter.ToInt32(BitConverter.GetBytes(selected.m_baseHeights[index]),0)!=BitConverter.ToInt32(BitConverter.GetBytes(full.m_baseHeights[index]),0))throw new InvalidDataException("Selective native builder differs from full builder.");count++;
                }
            }
            return count;
        }
    }
}
