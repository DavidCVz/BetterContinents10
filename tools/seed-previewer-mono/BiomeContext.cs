using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using BC=BetterContinents.BetterContinents;

// Only authored assets are imported. No reference points, heights or sector assignments
// are installed in the generator: those are independent comparison targets.
static class BiomeContext
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static object F(float value)=>SeedPreviewerDiagnostics.PrecisionTrace.Float(value);
    static string Bits(float value)=>BitConverter.ToInt32(BitConverter.GetBytes(value),0).ToString("X8");
    static float Float(JToken token)
    {
        string bits=(string)token["bits"];
        if(bits==null||bits.Length!=8)throw new InvalidDataException("Invalid catalog float.");
        float value=BitConverter.ToSingle(BitConverter.GetBytes(Convert.ToUInt32(bits,16)),0);
        if(float.IsNaN(value)||float.IsInfinity(value)||Bits((float)token["value"])!=bits)throw new InvalidDataException("Catalog float bits/value mismatch.");
        return value;
    }
    public static bool InitializeAndCompare(World world,WorldGenerator generator,string reference,string output)
    {
        var context=JObject.Parse(File.ReadAllText(Path.Combine(reference,"biome-context.json")));
        if((string)context["format"]!="sp-biome-context/1"||!(bool)context["worldData"]["ready"])throw new InvalidDataException("Ready biome-context reference required.");
        if(context["sectors"].Count()!=12675||context["worldSectors"].Count()!=(int)context["worldData"]["sectorCount"])throw new InvalidDataException("Incomplete sector reference.");
        var catalog=new List<AltBiome>();var names=new HashSet<string>();
        foreach(var definition in context["definitions"]){
            var item=new AltBiome();var values=(JObject)definition["fields"];
            // Reject missing fields instead of silently accepting constructor defaults.
            var expected=typeof(AltBiome).GetFields(All).Where(f=>!f.IsStatic&&(f.Name.StartsWith("m_")||f.Name=="heightMapChanges")&&(f.FieldType.IsPrimitive||f.FieldType.IsEnum||f.FieldType==typeof(string)||f.FieldType==typeof(List<string>))).ToArray();
            if(values.Count!=expected.Length)throw new InvalidDataException("Catalog field inventory mismatch.");
            foreach(var field in expected){
                JToken token=values[field.Name];if(token==null)throw new InvalidDataException("Missing catalog field: "+field.Name);
                object value;
                if(field.FieldType==typeof(float))value=Float(token);
                else if(field.FieldType.IsEnum){
                    if((string)token["enumType"]!=field.FieldType.FullName)throw new InvalidDataException("Catalog enum type mismatch.");
                    value=Enum.ToObject(field.FieldType,Convert.ToInt32((string)token["bits"],16));
                }
                else if(field.FieldType==typeof(List<string>))value=token.Type==JTokenType.Null?null:token.Values<string>().ToList();
                else value=token.ToObject(field.FieldType);
                field.SetValue(item,value);
            }
            if(string.IsNullOrEmpty(item.m_name)||!names.Add(item.m_name))throw new InvalidDataException("Duplicate or empty catalog name.");
            catalog.Add(item);
        }
        if(catalog.Count==0)throw new InvalidDataException("Empty catalog.");
        typeof(AltBiomeList).GetField("m_altBiomes",All).SetValue(null,catalog);
        System.Console.WriteLine("SECTORS catalog="+catalog.Count+" generating points");
        // Use the mod's load preparation, but never VerifyBiomeData's cache/file wrapper.
        typeof(BC.AltBiomeControl).GetMethod("BeforeVerifyBiomeData",All).Invoke(null,new object[]{world});
        AltBiomeWorldData.GenerateBiomePoints(world);
        System.Console.WriteLine("SECTORS generating regions and modifiers");
        world.m_biomeData.GenerateSectors();
        if(!world.m_biomeData.IsReady)throw new InvalidOperationException("Offline sector generation incomplete.");
        var actual=world.m_biomeData.Sectors.Select(s=>new{biome=(int)s.Biome,centerX=F(s.Center.x),centerY=F(s.Center.y),minX=F(s.Min.x),minY=F(s.Min.y),maxX=F(s.Max.x),maxY=F(s.Max.y),edgeCount=s.EdgeCount,altNames=s.AltBiomes.Select(a=>a.m_name).ToArray(),heightMin=F(s.HeightMin),heightMax=F(s.HeightMax),heightAverage=F(s.HeightAvg)}).ToArray();
        var array=JArray.Parse(SeedPreviewerReference.ReferenceJson.Serialize(actual));
        File.WriteAllText(Path.Combine(output,"world-sectors.json"),array.ToString());
        var expectedSectors=(JArray)context["worldSectors"];
        int sectorDifferences=Math.Abs(array.Count-expectedSectors.Count);
        var differences=new List<string>();
        for(int i=0;i<Math.Min(array.Count,expectedSectors.Count);i++){
            // Float decimal formatting may vary; bit strings define equality.
            var left=array[i].DeepClone();var right=expectedSectors[i].DeepClone();
            foreach(var v in ((JContainer)left).DescendantsAndSelf().OfType<JObject>().Where(v=>v["bits"]!=null))v.Remove("value");
            foreach(var v in ((JContainer)right).DescendantsAndSelf().OfType<JObject>().Where(v=>v["bits"]!=null))v.Remove("value");
            if(!JToken.DeepEquals(left,right)){sectorDifferences++;if(differences.Count<20)differences.Add("sector "+i);}
        }
        int pilotDifferences=0;
        foreach(var point in context["sectors"]){
            var s=generator.GetBiomeSector((float)point["x"],(float)point["z"]);
            if((int)s.Biome!=(int)point["biome"]||!s.AltBiomes.Select(a=>a.m_name).SequenceEqual(point["altNames"].Values<string>())||Bits(s.HeightMin)!=(string)point["min"]["bits"]||Bits(s.HeightMax)!=(string)point["max"]["bits"]||Bits(s.HeightAvg)!=(string)point["average"]["bits"])pilotDifferences++;
        }
        var data=world.m_biomeData;
        bool countsMatch=data.Size==(int)context["worldData"]["size"]&&data.Sectors.Count==(int)context["worldData"]["sectorCount"]&&data.Sectors.Count(s=>s.AltBiomes.Count>0)==(int)context["worldData"]["modifiedSectors"];
        File.WriteAllText(Path.Combine(output,"sector-comparison.json"),SeedPreviewerReference.ReferenceJson.Serialize(new{catalogCount=catalog.Count,ready=data.IsReady,size=data.Size,sectorCount=data.Sectors.Count,modifiedSectors=data.Sectors.Count(s=>s.AltBiomes.Count>0),countsMatch,sectorDifferences,pilotSectorPoints=context["sectors"].Count(),pilotDifferences,firstDifferences=differences,status="sector-diagnostic-not-global-terrain-acceptance"}));
        System.Console.WriteLine("SECTORS count="+data.Sectors.Count+" differences="+sectorDifferences+" pilotDifferences="+pilotDifferences);
        return countsMatch&&sectorDifferences==0&&pilotDifferences==0;
    }
}
