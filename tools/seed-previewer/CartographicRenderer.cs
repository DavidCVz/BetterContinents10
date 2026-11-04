using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// Pure presentation of the verified M1 layers. No game, worker or native binding calls.
internal static class CartographicRenderer
{
    internal const string Format="sp-cartographic-render/1";
    static readonly string[] Names={"Meadows","Swamp","Mountain","BlackForest","Plains","AshLands","DeepNorth","Ocean","Mistlands"};
    static Rgba32 Mix(Rgba32 a,Rgba32 b,double t){t=Math.Clamp(t,0,1);return new(Byte(a.R+(b.R-a.R)*t),Byte(a.G+(b.G-a.G)*t),Byte(a.B+(b.B-a.B)*t));}
    static byte Byte(double n)=>(byte)Math.Clamp(Math.Round(n),0,255);
    static Rgba32 Scale(Rgba32 c,double f)=>new(Byte(c.R*f),Byte(c.G*f),Byte(c.B*f));
    internal static Image<Rgba32> Draw(CartographicMapData d,CartographicStyle s)
    {
        int n=d.Size;double step=(double)d.Span/n;float[] colors=CartographicFade.Compute(d,s);
        var shallow=Rgba32.ParseHex(s.Shallow);var shelf=Rgba32.ParseHex(s.Shelf);var deep=Rgba32.ParseHex(s.Deep);
        var sand=Rgba32.ParseHex(s.Sand);var snow=Rgba32.ParseHex(s.Snow);var background=Rgba32.ParseHex(s.Background);
        var image=new Image<Rgba32>(n,n);
        for(int row=0;row<n;row++)for(int col=0;col<n;col++){
            int i=row*n+col,x0=Math.Max(0,col-1),x1=Math.Min(n-1,col+1),z0=Math.Max(0,row-1),z1=Math.Min(n-1,row+1);
            double dx=((double)d.Height[row*n+x1]-d.Height[row*n+x0])/((x1-x0)*step);
            double dz=((double)d.Height[z0*n+col]-d.Height[z1*n+col])/((z1-z0)*step);
            double light=s.Shade(dx,dz),h=d.Height[i];Rgba32 color;
            if(d.Water[i]==1){
                double depth=30-h;color=depth<s.ShelfDepth?Mix(shallow,shelf,depth/s.ShelfDepth):Mix(shelf,deep,(depth-s.ShelfDepth)/s.DeepRange);
                color=Scale(color,.94+.06*light);
                bool edge=d.Water[row*n+x0]==0||d.Water[row*n+x1]==0||d.Water[z0*n+col]==0||d.Water[z1*n+col]==0;
                if(edge)color=Mix(color,shallow,s.ShoreTint);
            }else{
                int k=i*4;color=new(Byte(colors[k]),Byte(colors[k+1]),Byte(colors[k+2]));
                double ordinary=Math.Clamp((h-60)/900,0,.13),snowy=Math.Clamp((h-100)/230,0,.8);
                color=Mix(color,snow,ordinary+(snowy-ordinary)*colors[k+3]);
                color=Mix(color,sand,s.ShoreTint*Math.Exp(-Math.Max(0,h-30)/s.ShoreHeight));color=Scale(color,light);
            }
            double wx=(col+.5)*step-d.Span/2,wz=d.Span/2-(row+.5)*step;
            double coverage=s.Circular?Math.Clamp((d.Span/2-Math.Sqrt(wx*wx+wz*wz))/step+.5,0,1):1;
            if(s.Transparent)color.A=Byte(255*coverage);else color=Mix(background,color,coverage);
            image[col,row]=color;
        }
        return image;
    }
    internal static int Run(string bundle,string output,string? stylePath)
    {
        bundle=Path.TrimEndingDirectorySeparator(Path.GetFullPath(bundle));output=Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        if(output.Equals(bundle,StringComparison.OrdinalIgnoreCase)||output.StartsWith(bundle+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Render output must be outside the source bundle.");
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Render output must be new.");
        var clock=Stopwatch.StartNew();var data=CartographicMapData.Read(bundle);var style=CartographicStyle.Read(stylePath);
        using var image=Draw(data,style);
        string styleJson=JsonSerializer.Serialize(style,CartographicStyle.Json);
        byte[] pixels=new byte[data.Size*data.Size*4];image.CopyPixelDataTo(pixels);
        string pixelHash=Convert.ToHexString(SHA256.HashData(pixels));
        string parent=Path.GetDirectoryName(output)??throw new IOException("Output needs a parent directory.");Directory.CreateDirectory(parent);
        string staging=Path.Combine(parent,"."+Path.GetFileName(output)+".partial-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        try{
            image.SaveAsPng(Path.Combine(staging,"map.png"));File.WriteAllText(Path.Combine(staging,"style-used.json"),styleJson);
            var palette=CartographicMapData.KnownBiomes.Select((id,i)=>new{id,name=Names[i],color=style.Palette[id]}).ToArray();
            var legend=new{format="sp-cartographic-legend/1",mode="base-terrain",palette,oceanPlaneMetres=30,shallow=style.Shallow,shelf=style.Shelf,deep=style.Deep,shelfDepthMetres=style.ShelfDepth,deepDepthMetres=style.ShelfDepth+style.DeepRange,style.FadeMetres,fadeDefinition="Nominal 10-90% straight land-boundary width; no-flux diffusion excludes water edges. Raster spacing and land topology affect the observed width.",orientation="north-first pixel centers",metresPerPixel=(double)data.Span/data.Size,limitation="Colors include relief, coast and altitude tint. Blended colors are not categorical biome IDs. No forest density, material masks or alt-biome visual overrides."};
            File.WriteAllText(Path.Combine(staging,"legend.json"),JsonSerializer.Serialize(legend,CartographicStyle.Json));
            string legendText="# Base terrain cartography\n\nNorth is up (+Z); east is right (+X). Elevation is in metres, ocean plane 30 m.\n\n| Biome ID | Name | Base color |\n|---|---|---|\n"+string.Concat(palette.Select(p=>$"| {p.id} | {p.name} | {p.color} |\n"));
            legendText+="\nThe palette describes base colors, before lighting, altitude/shore tint and land-biome blending. Ocean shading follows actual depth and the water mask, including submerged non-Ocean biomes.\n\nFade is the nominal 10%-90% width of a straight boundary in metres. Land adjacency constrains it: no water edge is crossed, including straits between islands. It changes colors and snow-tint weights only. See style-used.json for the full recipe.\n\nThis base mode has no extracted forest density, lava/moss material masks or alt-biome visual overrides. It is not a tree/object-location map.\n";
            File.WriteAllText(Path.Combine(staging,"legend.md"),legendText);
            var outputs=new[]{"map.png","style-used.json","legend.json","legend.md"}.ToDictionary(f=>f,f=>CartographicMapData.Hash(Path.Combine(staging,f)));
            data.VerifyUnchanged(bundle);
            var manifest=new{format=Format,status="complete",mode="base-terrain",data.Preset,data.Seed,size=data.Size,totalSizeMetres=data.Span,orientation="north-first pixel centers",metresPerPixel=(double)data.Span/data.Size,sourceManifestSha256=data.ManifestHash,sourceLayers=data.Hashes,styleFormat=style.Format,styleSha256=outputs["style-used.json"],rendererSha256=CartographicMapData.Hash(typeof(CartographicRenderer).Assembly.Location),imageLibrarySha256=CartographicMapData.Hash(typeof(Image).Assembly.Location),runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,pixelFormat="RGBA8 row-major north-first",pixelSha256=pixelHash,outputs};
            File.WriteAllText(Path.Combine(staging,"render-manifest.json"),JsonSerializer.Serialize(manifest,CartographicStyle.Json));
            // Rename publishes a complete directory and refuses an existing destination.
            Directory.Move(staging,output);
        }catch{
            File.WriteAllText(Path.Combine(staging,"failure.json"),"{\"status\":\"failed-unpublished\"}");throw;
        }
        System.Console.WriteLine($"Cartographic render complete: {data.Size}x{data.Size}, {clock.ElapsedMilliseconds} ms; sources unchanged; RGBA SHA256 {pixelHash}");return 0;
    }
}
