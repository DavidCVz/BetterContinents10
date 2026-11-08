using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using BC=BetterContinents.BetterContinents;

namespace SeedPreviewerReference
{
    // Reads active state only. No material assignment, texture readback or generation patch.
    internal static class StyleReference
    {
        const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        static readonly int[] Biomes={1,2,4,8,16,32,64,256,512};
        static readonly string[] Fields={"m_meadowsColor","m_swampColor","m_mountainColor","m_blackforestColor","m_heathColor","m_ashlandsColor","m_deepnorthColor",null,"m_mistlandsColor"};
        static string Hash(string p){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(File.ReadAllBytes(p))).Replace("-","");}
        static string Bits(float v)=>BitConverter.ToInt32(BitConverter.GetBytes(v),0).ToString("X8");
        static object F(float v){if(float.IsNaN(v)||float.IsInfinity(v))throw new InvalidDataException("Non-finite style property.");return SeedPreviewerDiagnostics.PrecisionTrace.Float(v);}
        static object ColorData(Color c){Color32 bytes=c;return new{raw=new[]{F(c.r),F(c.g),F(c.b),F(c.a)},rgba8=new[]{(int)bytes.r,bytes.g,bytes.b,bytes.a},hexRgba=bytes.r.ToString("X2")+bytes.g.ToString("X2")+bytes.b.ToString("X2")+bytes.a.ToString("X2"),conversion="Unity Color32 implicit operator: Clamp01, float32 *255, Mathf.Round; no explicit gamma transform; not final shader RGB"};}
        static bool Equal(Color a,Color b)=>Bits(a.r)==Bits(b.r)&&Bits(a.g)==Bits(b.g)&&Bits(a.b)==Bits(b.b)&&Bits(a.a)==Bits(b.a);
        static Color Field(Minimap map,string name)=>(Color)(typeof(Minimap).GetField(name,All)??throw new MissingFieldException(name)).GetValue(map);
        static object Palette(Minimap map,out Color[] values)
        {
            var method=typeof(Minimap).GetMethod("GetPixelColor",All,null,new[]{typeof(Heightmap.Biome)},null)??throw new MissingMethodException("GetPixelColor");
            var rows=new List<object>();values=new Color[Biomes.Length];
            for(int i=0;i<Biomes.Length;i++){
                Color expected=Fields[i]==null?Color.white:Field(map,Fields[i]);
                Color native=(Color)method.Invoke(map,new object[]{(Heightmap.Biome)Biomes[i]});
                if(!Equal(native,expected))throw new InvalidDataException("Native pixel color disagrees with field mapping.");
                values[i]=native;rows.Add(new{biome=Biomes[i],field=Fields[i],expectedSource=Fields[i]==null?"Unity Color.white fallback":"active Minimap field",expected=ColorData(expected),native=ColorData(native)});
            }
            var patch=HarmonyLib.Harmony.GetPatchInfo(method);
            return new{frame=Time.frameCount,rows,patchOwners=patch==null?new string[0]:patch.Owners.ToArray(),forestColor=ColorData(Field(map,"s_forestColor")),noForestColor=ColorData(Field(map,"s_noForestColor"))};
        }
        static object TextureInfo(Texture t)
        {
            if(t==null)return new{status="unassigned"};
            var t2=t as Texture2D;
            return new{status="reference-only-no-pixels",instanceId=t.GetInstanceID(),name=t.name,type=t.GetType().FullName,width=t.width,height=t.height,dimension=t.dimension.ToString(),filter=t.filterMode.ToString(),wrap=t.wrapMode.ToString(),aniso=t.anisoLevel,format=t2==null?null:t2.format.ToString(),readable=t2==null?(bool?)null:t2.isReadable,mipCount=t2==null?(int?)null:t2.mipmapCount};
        }
        static object MaterialInfo(string source,Material material)
        {
            if(material==null)return new{source,status="unassigned"};
            try{
                var shader=material.shader;if(shader==null)return new{source,status="no-shader"};
                int count=shader.GetPropertyCount();var properties=new List<object>();
                for(int i=0;i<Math.Min(count,128);i++){
                    string name=shader.GetPropertyName(i),kind=shader.GetPropertyType(i).ToString();
                    try{
                        object value;
                        switch(kind){
                            case "Color":value=ColorData(material.GetColor(name));break;
                            case "Vector":var v=material.GetVector(name);value=new[]{F(v.x),F(v.y),F(v.z),F(v.w)};break;
                            case "Float":case "Range":value=F(material.GetFloat(name));break;
                            case "Int":value=material.GetInt(name);break;
                            case "Texture":var scale=material.GetTextureScale(name);var offset=material.GetTextureOffset(name);value=new{texture=TextureInfo(material.GetTexture(name)),scale=new[]{F(scale.x),F(scale.y)},offset=new[]{F(offset.x),F(offset.y)}};break;
                            default:properties.Add(new{name,kind,status="unsupported-property-kind"});continue;
                        }
                        properties.Add(new{name,kind,status="observed",value});
                    }catch(Exception e){properties.Add(new{name,kind,status="unavailable",reason=e.GetType().Name});}
                }
                return new{source,status=count>128?"partial-property-limit":"observed-basic-inventory",instanceId=material.GetInstanceID(),name=material.name,shader=shader.name,shaderId=shader.GetInstanceID(),keywords=material.shaderKeywords,propertyCount=count,properties};
            }catch(Exception e){return new{source,status="unavailable",reason=e.GetType().Name};}
        }
        static object UiInfo(Minimap map,string field)
        {
            try{
                object ui=typeof(Minimap).GetField(field,All).GetValue(map);
                if(ui==null)return new{source=field,status="unassigned"};
                var material=(Material)ui.GetType().GetProperty("material").GetValue(ui,null);
                object canvas=ui.GetType().GetProperty("canvasRenderer").GetValue(ui,null);
                int count=(int)canvas.GetType().GetProperty("materialCount").GetValue(canvas,null);
                Material bound=count>0?(Material)canvas.GetType().GetMethod("GetMaterial",new[]{typeof(int)}).Invoke(canvas,new object[]{0}):null;
                return new{source=field,status="observed-no-assignment",configured=MaterialInfo(field+".material",material),bound=MaterialInfo(field+".canvasRenderer[0]",bound),renderedMaterialCount=count,limitation="Only first bound material, if already assigned. No materialForRendering getter or forced UI update; reference identity is capture-local."};
            }catch(Exception e){return new{source=field,status="unavailable",reason=e.GetType().Name};}
        }
        static object Inventory(Minimap map)=>new{frame=Time.frameCount,fieldMaterials=new[]{MaterialInfo("m_mapSmallShader",(Material)typeof(Minimap).GetField("m_mapSmallShader",All).GetValue(map)),MaterialInfo("m_mapLargeShader",(Material)typeof(Minimap).GetField("m_mapLargeShader",All).GetValue(map))},ui=new[]{UiInfo(map,"m_mapImageSmall"),UiInfo(map,"m_mapImageLarge")},generatedInputs=new[]{"m_mapTexture","m_forestMaskTexture","m_heightTexture","m_fogTexture"}.Select(name=>new{source=name,texture=TextureInfo((Texture)typeof(Minimap).GetField(name,All).GetValue(map))}).ToArray(),limitation="Basic references and values only; no pixels, original asset container identity, compiled shader recovery or final pixel-color claim."};
        static IEnumerator Core()
        {
            var map=Minimap.instance;var generator=WorldGenerator.instance;
            if(map==null||generator==null||generator.m_world.m_seedName!="AbC123")throw new InvalidOperationException("Load the disposable AbC123 test world first.");
            string output=Path.Combine(BepInEx.Paths.BepInExRootPath,"seed-previewer-style-reference",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
            var before=Palette(map,out var colors);yield return null;yield return null;
            if(Minimap.instance!=map||WorldGenerator.instance!=generator)throw new InvalidOperationException("World or minimap changed.");
            var after=Palette(map,out var repeated);
            if(!colors.Zip(repeated,Equal).All(b=>b))throw new InvalidDataException("Palette changed between frames.");
            File.WriteAllText(Path.Combine(output,"palette.json"),ReferenceJson.Serialize(new{format="sp-native-map-palette/1",first=before,second=after,colorSpace=QualitySettings.activeColorSpace.ToString(),limitation="Effective base colors in this profile; neither final shader pixels nor vanilla defaults established by field names."}));
            File.WriteAllText(Path.Combine(output,"material-inventory.json"),ReferenceJson.Serialize(Inventory(map)));
            var settings=new List<string>();BC.Settings.Dump(settings.Add);File.WriteAllLines(Path.Combine(output,"effective-settings.txt"),settings);
            File.WriteAllText(Path.Combine(output,"manifest.json"),ReferenceJson.Serialize(new{format="sp-native-map-style/1",status="complete-palette-basic-inventory",seed=generator.m_world.m_seedName,gameVersion=Application.version,unityVersion=Application.unityVersion,gameSha256=Hash(typeof(World).Assembly.Location),coreSha256=Hash(typeof(Color).Assembly.Location),modSha256=Hash(typeof(BC).Assembly.Location),probeSha256=Hash(typeof(StyleReference).Assembly.Location),paletteSha256=Hash(Path.Combine(output,"palette.json")),inventorySha256=Hash(Path.Combine(output,"material-inventory.json")),settingsSha256=Hash(Path.Combine(output,"effective-settings.txt")),plugins=BepInEx.Bootstrap.Chainloader.PluginInfos.Values.Select(p=>new{guid=p.Metadata.GUID,version=p.Metadata.Version.ToString()}).ToArray(),paletteBiomes=9,stablePaletteReads=2,texturePixelsExported=false}));
            global::Console.instance?.Print("Native map style captured: 9 biome colors, two stable reads.");
        }
        public static IEnumerator Capture(Action finished,Action<string> error)
        {
            var iterator=Core();try{
                while(true){bool more=false,failed=false;object current=null;
                    try{more=iterator.MoveNext();if(more)current=iterator.Current;}catch(Exception e){error(e.ToString());global::Console.instance?.Print("Native map style capture failed; inspect BepInEx log.");failed=true;}
                    if(failed||!more)yield break;yield return current;
                }
            }finally{(iterator as IDisposable)?.Dispose();finished();}
        }
    }
}
