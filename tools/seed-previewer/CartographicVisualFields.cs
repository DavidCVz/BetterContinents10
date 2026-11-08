using System.Text.Json;
using SixLabors.ImageSharp.PixelFormats;

internal sealed class CartographicVisualFields
{
    internal required float[] Forest,Alpha;
    internal required string ManifestHash;
    internal required Dictionary<string,string> Hashes;
    internal static CartographicVisualFields Read(string path,CartographicMapData d)
    {
        string manifest=Path.Combine(path,"visual-manifest.json");string manifestHash=CartographicMapData.Hash(manifest);
        if(new FileInfo(manifest).Length>1024*1024)throw new InvalidDataException("Visual manifest too large.");
        using var doc=JsonDocument.Parse(File.ReadAllText(manifest));var m=doc.RootElement;
        if(m.GetProperty("format").GetString()!="sp-visual-fields/1"||m.GetProperty("status").GetString()!="complete"||m.GetProperty("parentManifestSha256").GetString()!=d.ManifestHash||m.GetProperty("preset").GetString()!=d.Preset||m.GetProperty("seed").GetString()!=d.Seed||m.GetProperty("size").GetInt32()!=d.Size||m.GetProperty("totalSizeMetres").GetSingle()!=d.Span||m.GetProperty("orientation").GetString()!="north-first pixel centers"||m.GetProperty("textureOverrides").GetString()!="not-applicable-all-32-authored-definitions-none")throw new InvalidDataException("Visual parent/domain mismatch.");
        if(m.GetProperty("sampling").GetString()!="point procedural controls: GetForestFactor(X,0,Z), GetBiomeHeight(GetBiome(X,Z),X,Z). Raw RGBA before texture quantization, terrain edits or GPU filtering; not the blended terrain paint texture.")throw new InvalidDataException("Unsupported visual sampling contract.");
        foreach(var source in d.Hashes)if(m.GetProperty("sourceLayers").GetProperty(source.Key).GetString()!=source.Value)throw new InvalidDataException("Visual parent layer mismatch.");
        foreach(string key in new[]{"workerSha256","referenceManifestSha256","comparisonSha256","monoSha256","contextSha256"})if(!System.Text.RegularExpressions.Regex.IsMatch(m.GetProperty(key).GetString()??"","^[0-9A-F]{64}$"))throw new InvalidDataException("Missing visual provenance.");
        var hashes=new Dictionary<string,string>();int count=d.Size*d.Size;
        foreach(var file in new[]{("forest.f32",4), ("ground.rgba.f32",16), ("variants.i32",4)}){
            string f=Path.Combine(path,file.Item1);if(new FileInfo(f).Length!=(long)count*file.Item2)throw new InvalidDataException("Visual dimensions mismatch.");
            string hash=CartographicMapData.Hash(f);if(hash!=m.GetProperty("files").GetProperty(file.Item1).GetString())throw new InvalidDataException("Visual layer integrity failure.");hashes.Add(file.Item1,hash);
        }
        int classes=m.GetProperty("classes").GetArrayLength();if(classes<1||classes>1024||m.GetProperty("classes")[0].GetArrayLength()!=0)throw new InvalidDataException("Invalid variant classes.");
        var combinations=new HashSet<string>(StringComparer.Ordinal);
        foreach(var entry in m.GetProperty("classes").EnumerateArray()){
            var names=entry.EnumerateArray().Select(e=>e.GetString()).ToArray();
            if(names.Any(s=>string.IsNullOrEmpty(s)||s.Length>128||s.Contains('\n'))||names.Distinct(StringComparer.Ordinal).Count()!=names.Length||!combinations.Add(string.Join("\n",names)))throw new InvalidDataException("Invalid variant class table.");
        }
        var forest=new float[count];var alpha=new float[count];
        using(var f=new BinaryReader(File.OpenRead(Path.Combine(path,"forest.f32"))))
        using(var g=new BinaryReader(File.OpenRead(Path.Combine(path,"ground.rgba.f32"))))
        using(var v=new BinaryReader(File.OpenRead(Path.Combine(path,"variants.i32"))))for(int i=0;i<count;i++){
            forest[i]=f.ReadSingle();if(!float.IsFinite(forest[i]))throw new InvalidDataException("Non-finite forest factor.");
            for(int channel=0;channel<4;channel++){float value=g.ReadSingle();if(!float.IsFinite(value))throw new InvalidDataException("Non-finite ground control.");if(channel==3)alpha[i]=value;}
            int id=v.ReadInt32();if(id<0||id>=classes)throw new InvalidDataException("Unknown variant class.");
        }
        var result=new CartographicVisualFields{Forest=forest,Alpha=alpha,ManifestHash=manifestHash,Hashes=hashes};result.Verify(path);return result;
    }
    internal void Verify(string path){if(CartographicMapData.Hash(Path.Combine(path,"visual-manifest.json"))!=ManifestHash)throw new IOException("Visual manifest changed.");foreach(var entry in Hashes)if(CartographicMapData.Hash(Path.Combine(path,entry.Key))!=entry.Value)throw new IOException("Visual layer changed.");}
}

internal sealed class CartographicVisualStyle
{
    public string Format {get;set;}="sp-visual-style/1";
    public bool Forest {get;set;}=true;
    public bool Ground {get;set;}=true;
    public double ForestStrength {get;set;}=.35;
    public double MossStrength {get;set;}=.65;
    public double LavaStrength {get;set;}=.9;
    public string ForestColor {get;set;}="#263E2C";
    public string MossColor {get;set;}="#71834A";
    public string LavaColor {get;set;}="#E76A25";
    internal static CartographicVisualStyle Read(string? path)
    {
        CartographicVisualStyle s;
        if(path==null)s=new();else{
            if(new FileInfo(path).Length>65536)throw new InvalidDataException("Visual style too large.");
            string text=File.ReadAllText(path);using var doc=JsonDocument.Parse(text);CartographicStyle.CheckKeys(doc.RootElement);
            if(doc.RootElement.GetProperty("format").GetString()!="sp-visual-style/1")throw new InvalidDataException("Visual style format required.");
            s=JsonSerializer.Deserialize<CartographicVisualStyle>(text,CartographicStyle.Json)??throw new InvalidDataException("Empty visual style.");
        }
        if(s.Format!="sp-visual-style/1")throw new InvalidDataException("Invalid visual style format.");
        foreach(double n in new[]{s.ForestStrength,s.MossStrength,s.LavaStrength})if(!double.IsFinite(n)||n<0||n>1)throw new InvalidDataException("Invalid visual strength.");
        foreach(string c in new[]{s.ForestColor,s.MossColor,s.LavaColor})if(c==null||!System.Text.RegularExpressions.Regex.IsMatch(c,"^#[0-9A-Fa-f]{6}$"))throw new InvalidDataException("Invalid visual color.");return s;
    }
}
