using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;

internal static class TerrainImages
{
    internal static int Run(string folder)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"map.json")));
        int size=document.RootElement.GetProperty("size").GetInt32();
        if(size<64||size>2048)throw new InvalidDataException("Invalid terrain image size.");
        var palette=new Dictionary<int,string>{{1,"#74A45A"},{2,"#716143"},{4,"#D6DCE5"},{8,"#26492F"},{16,"#CAB16A"},{32,"#9C4234"},{64,"#93C6DA"},{256,"#234F75"},{512,"#766C91"}};
        foreach(var entry in new[]{("surface.f32",4),("biomes.i32",4),("below-ocean.u8",1)})if(new FileInfo(Path.Combine(folder,entry.Item1)).Length!=(long)size*size*entry.Item2)throw new InvalidDataException("Terrain layer size mismatch.");
        using var biomes=new Image<Rgba32>(size,size);using var water=new Image<L8>(size,size);
        using var b=new BinaryReader(File.OpenRead(Path.Combine(folder,"biomes.i32")));using var w=new BinaryReader(File.OpenRead(Path.Combine(folder,"below-ocean.u8")));
        for(int row=0;row<size;row++)for(int col=0;col<size;col++){
            int biome=b.ReadInt32();biomes[col,row]=Rgba32.ParseHex(palette.GetValueOrDefault(biome,"#FF00FF"));
            byte below=w.ReadByte();if(below>1)throw new InvalidDataException("Invalid water mask.");water[col,row]=new L8((byte)(below*255));
        }
        foreach(string file in new[]{"surface.png","biomes.png","below-ocean.png","biome-palette.json"})if(File.Exists(Path.Combine(folder,file)))throw new IOException("Terrain images must be new.");
        BaseImage.Run(Path.Combine(folder,"surface.f32"),size.ToString(),Path.Combine(folder,"surface.png"));
        biomes.SaveAsPng(Path.Combine(folder,"biomes.png"));water.SaveAsPng(Path.Combine(folder,"below-ocean.png"));
        File.WriteAllText(Path.Combine(folder,"biome-palette.json"),JsonSerializer.Serialize(new{palette,unknown="#FF00FF",surface="L16 autoscaled min/max per map; use surface.f32 for metres",water="white below static ocean 30m, black above"}));
        return 0;
    }
}
