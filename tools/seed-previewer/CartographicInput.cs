using System.Security.Cryptography;
using System.Text.Json;

internal sealed class CartographicMapData
{
    internal required int Size;
    internal required float Span;
    internal required float[] Height;
    internal required int[] Biome;
    internal required byte[] Water;
    internal required Dictionary<string,string> Hashes;
    internal required string Preset;
    internal required string Seed;
    internal required string ManifestHash;
    internal static readonly int[] KnownBiomes={1,2,4,8,16,32,64,256,512};
    internal static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    internal static CartographicMapData Read(string bundle)
    {
        string manifestPath=Path.Combine(bundle,"manifest.json");
        if(new FileInfo(manifestPath).Length>1024*1024)throw new InvalidDataException("Manifest too large.");
        string manifestHash=Hash(manifestPath);
        using var manifest=JsonDocument.Parse(File.ReadAllText(manifestPath));var root=manifest.RootElement;
        if(root.GetProperty("Status").GetString()!="offline-terrain-map-not-global-game-parity")throw new InvalidDataException("Expected a complete M1 terrain bundle.");
        // Read only fixed layer names: manifest entries never become filesystem paths.
        var files=root.GetProperty("Files").EnumerateArray().ToDictionary(e=>e.GetProperty("File").GetString()!,e=>e.GetProperty("Sha256").GetString()!,StringComparer.Ordinal);
        var hashes=new Dictionary<string,string>();
        foreach(string name in new[]{"surface.f32","biomes.i32","below-ocean.u8","map.json"}){
            string path=Path.Combine(bundle,"map",name);long length=new FileInfo(path).Length;
            if(length>64L*1024*1024)throw new InvalidDataException("Source layer too large.");
            string hash=Hash(path);if(!files.TryGetValue(name,out string? expected)||hash!=expected)throw new InvalidDataException("Source layer integrity failure: "+name);hashes.Add(name,hash);
        }
        using var info=JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle,"map/map.json")));var data=info.RootElement;
        int size=data.GetProperty("size").GetInt32();float span=data.GetProperty("totalSizeMetres").GetSingle();
        string preset=data.GetProperty("preset").GetString()!,seed=data.GetProperty("seed").GetString()!;
        if(size<64||size>2048||!float.IsFinite(span)||span<=0||root.GetProperty("Resolution").GetInt32()!=size||data.GetProperty("orientation").GetString()!="north-first pixel centers")throw new InvalidDataException("Unsupported map geometry.");
        if(data.GetProperty("status").GetString()!="offline-map-not-global-game-parity"||data.GetProperty("surface").GetString()!="unmodified non-LOD 1m triangle mesh; sparse vertex evaluation"||data.GetProperty("water").GetString()!="below static 30m ocean plane; excludes volumes and waves")throw new InvalidDataException("Unsupported source contract.");
        if(root.GetProperty("Preset").GetString()!=preset||root.GetProperty("Seed").GetString()!=seed||!new[]{"Continents","Pangaea","Swirls","Vanillaish"}.Contains(preset)||seed.Length is <1 or >10||!seed.All(char.IsAsciiLetterOrDigit))throw new InvalidDataException("Source identity mismatch.");
        foreach(var layer in new[]{("surface.f32",4),("biomes.i32",4),("below-ocean.u8",1)})if(new FileInfo(Path.Combine(bundle,"map",layer.Item1)).Length!=(long)size*size*layer.Item2)throw new InvalidDataException("Source dimensions mismatch.");
        var height=new float[size*size];var biome=new int[height.Length];byte[] water=File.ReadAllBytes(Path.Combine(bundle,"map/below-ocean.u8"));
        using(var h=new BinaryReader(File.OpenRead(Path.Combine(bundle,"map/surface.f32"))))
        using(var b=new BinaryReader(File.OpenRead(Path.Combine(bundle,"map/biomes.i32"))))for(int i=0;i<height.Length;i++){
            height[i]=h.ReadSingle();biome[i]=b.ReadInt32();
            if(!float.IsFinite(height[i])||water[i]>1||water[i]!=(height[i]<30?1:0)||!KnownBiomes.Contains(biome[i]))throw new InvalidDataException("Invalid surface, ocean mask or biome ID.");
        }
        if(Hash(manifestPath)!=manifestHash)throw new IOException("Source manifest changed during read.");
        return new(){Size=size,Span=span,Height=height,Biome=biome,Water=water,Hashes=hashes,Preset=preset,Seed=seed,ManifestHash=manifestHash};
    }
    internal void VerifyUnchanged(string bundle)
    {
        if(Hash(Path.Combine(bundle,"manifest.json"))!=ManifestHash)throw new IOException("Source manifest changed.");
        foreach(var file in Hashes)if(Hash(Path.Combine(bundle,"map",file.Key))!=file.Value)throw new IOException("Source layer changed.");
    }
}
