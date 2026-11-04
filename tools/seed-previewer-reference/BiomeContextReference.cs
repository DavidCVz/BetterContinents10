using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using BC=BetterContinents.BetterContinents;

namespace SeedPreviewerReference
{
    // Authored asset values and live sector reference, independent of offline generation.
    internal static class BiomeContextReference
    {
        const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        static object F(float value)=>SeedPreviewerDiagnostics.PrecisionTrace.Float(value);
        public static object Capture(World world,WorldGenerator generator)
        {
            var source=typeof(AltBiomeList).GetField("m_altBiomes",All);
            if(source==null||!source.IsStatic)throw new MissingFieldException("AltBiomeList.m_altBiomes");
            var catalog=(IEnumerable)source.GetValue(null);
            if(catalog==null)throw new InvalidOperationException("Alt-biome catalog not initialized.");
            var definitions=new List<object>();
            foreach(var item in catalog){
                var values=new Dictionary<string,object>();var excluded=new List<string>();
                foreach(var field in typeof(AltBiome).GetFields(All).Where(f=>!f.IsStatic&&(f.Name.StartsWith("m_")||f.Name=="heightMapChanges"))){
                    object value=field.GetValue(item);var type=field.FieldType;
                    if(type==typeof(float))values[field.Name]=F((float)value);
                    else if(type.IsEnum)values[field.Name]=new{enumType=type.FullName,bits=Convert.ToInt32(value).ToString("X8")};
                    else if(type.IsPrimitive||type==typeof(string))values[field.Name]=value;
                    else if(type==typeof(List<string>))values[field.Name]=((List<string>)value)?.ToArray();
                    else excluded.Add(field.Name+":"+type.FullName);
                }
                definitions.Add(new{fields=values,excludedNonTerrainFields=excluded});
            }
            var sectors=new List<object>();
            foreach(var zone in new[]{new Vector2s(0,0),new Vector2s(16,-7),new Vector2s(-83,59)})
            for(int row=0;row<=64;row++)for(int col=0;col<=64;col++){
                float x=zone.x*64-32+col,z=zone.y*64-32+row;var sector=generator.GetBiomeSector(x,z);
                sectors.Add(new{zoneX=zone.x,zoneZ=zone.y,x,z,biome=(int)sector.Biome,altNames=sector.AltBiomes.Select(a=>a.m_name).ToArray(),min=F(sector.HeightMin),max=F(sector.HeightMax),average=F(sector.HeightAvg)});
            }
            var data=world.m_biomeData;
            var worldSectors=new List<object>();
            if(data!=null)foreach(var sector in data.Sectors){
                worldSectors.Add(new{biome=(int)sector.Biome,centerX=F(sector.Center.x),centerY=F(sector.Center.y),minX=F(sector.Min.x),minY=F(sector.Min.y),maxX=F(sector.Max.x),maxY=F(sector.Max.y),edgeCount=sector.EdgeCount,altNames=sector.AltBiomes.Select(a=>a.m_name).ToArray(),heightMin=F(sector.HeightMin),heightMax=F(sector.HeightMax),heightAverage=F(sector.HeightAvg)});
            }
            var water=ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<WaterVolume>();
            object prefabWater=water==null?null:new{localY=F(water.transform.localPosition.y),worldY=F(water.transform.position.y)};
            return new{
                format="sp-biome-context/1",definitions,sectors,worldSectors,
                worldData=new{present=data!=null,ready=data!=null&&data.IsReady,size=data==null?0:data.Size,sectorCount=data==null?0:data.Sectors.Count,modifiedSectors=data==null?0:data.Sectors.Count(s=>s.AltBiomes.Count>0)},
                water=new{baselineMetres=F(ZoneSystem.instance.m_waterLevel),prefabWater,hasBetterContinentsLakes=BC.Settings.HasLakes,limitation="Static ocean/prefab reference, not instantaneous waves or coverage of arbitrary scene water volumes."},
                limitation="Authored scalar/enum/string data and pilot sectors only; excludes spawn/vegetation/location asset objects. No player identifiers or save paths."
            };
        }
    }
}
