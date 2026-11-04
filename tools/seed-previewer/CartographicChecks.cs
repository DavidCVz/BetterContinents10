using System.Text.Json;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

internal static class CartographicChecks
{
    // Mathematical presentation fixtures, never substitutes for world-generation evidence.
    static CartographicMapData Plane(int n,float span)=>new(){Size=n,Span=span,Height=Enumerable.Repeat(80f,n*n).ToArray(),Biome=Enumerable.Repeat(1,n*n).ToArray(),Water=new byte[n*n],Hashes=new(),Preset="Continents",Seed="MathOnly",ManifestHash=""};
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static double Width(CartographicMapData d,CartographicStyle s)
    {
        int n=d.Size;for(int row=0;row<n;row++)for(int col=n/2;col<n;col++)d.Biome[row*n+col]=4;
        s.Palette[1]="#000000";s.Palette[4]="#FFFFFF";float[] a=CartographicFade.Compute(d,s);
        double Cross(double target){for(int col=1;col<n;col++){double lo=a[(n/2*n+col-1)*4]/255,hi=a[(n/2*n+col)*4]/255;if(lo<=target&&hi>=target&&hi>lo)return(col-1+(target-lo)/(hi-lo))*d.Span/n;}throw new InvalidOperationException("Missing fade crossing.");}
        return Cross(.9)-Cross(.1);
    }
    internal static int Run()
    {
        var s=new CartographicStyle();s.Validate();CartographicSample.CheckGeometry();
        Require(s.Shade(1,0)>s.Shade(-1,0)&&s.Shade(0,-1)>s.Shade(0,1),"Style lighting axes reversed.");
        Require(Math.Abs(s.Shade(0,0)-(.48+.74*Math.Sqrt(.5)))<1e-12,"Flat-plane illumination.");
        var d=Plane(64,1280);float[] constant=CartographicFade.Compute(d,s);
        Require(constant.Where((v,i)=>i%4==0).All(v=>v==120),"Constant land color changed.");
        for(int y=0;y<64;y++)for(int x=0;x<64;x++){
            int i=y*64+x;if(x==31||x==32){d.Water[i]=1;d.Height[i]=0;d.Biome[i]=256;}else if(x>32)d.Biome[i]=4;
        }
        var heights=d.Height.ToArray();var biomes=d.Biome.ToArray();var water=d.Water.ToArray();
        var faded=CartographicFade.Compute(d,s);
        for(int y=0;y<64;y++){
            Require(faded[(y*64+30)*4]==120&&faded[(y*64+33)*4]==188,"Fade crossed water between islands.");
            Require(faded[(y*64+31)*4]==61,"Water color field changed.");
        }
        var noFade=new CartographicStyle{FadeMetres=0};
        using(var before=CartographicRenderer.Draw(d,noFade))using(var after=CartographicRenderer.Draw(d,s)){
            for(int y=0;y<64;y++)for(int x=31;x<=32;x++)Require(before[x,y].Equals(after[x,y]),"Water pixels changed with land fade.");
            Require(before[0,0].A==255,"Opaque output has transparency.");
        }
        using(var alpha=CartographicRenderer.Draw(d,new(){Transparent=true}))Require(alpha[0,0].A==0&&alpha[32,32].A==255,"Circular alpha geometry.");
        using(var square=CartographicRenderer.Draw(d,new(){Transparent=true,Circular=false}))Require(square[0,0].A==255,"Square output clipped.");
        Require(d.Height.SequenceEqual(heights)&&d.Biome.SequenceEqual(biomes)&&d.Water.SequenceEqual(water),"Composition modified input arrays.");
        double coarse=Width(Plane(256,5120),s),fine=Width(Plane(512,5120),s);
        Require(Math.Abs(coarse-120)<20&&Math.Abs(fine-120)<10&&Math.Abs(coarse-fine)<20,"Metric transition width depends on resolution.");
        var step=Plane(64,1280);for(int y=0;y<64;y++)for(int x=32;x<64;x++)step.Biome[y*64+x]=4;
        var weights=CartographicFade.Compute(step,new());
        Require(weights[(32*64+31)*4+3]>0&&weights[(32*64+31)*4+3]<.5&&weights[(32*64+32)*4+3]>.5,"Snow-tint weights did not follow color fade.");
        System.Console.WriteLine(JsonSerializer.Serialize(new{status="passed",checks=new[]{"metric-light","constant-field","land-connectivity","water-pixel-invariance","circular-alpha","square-alpha","source-array-invariance","metric-fade-width","snow-tint-continuity"},nominalWidthMetres=120,coarseMeasuredMetres=coarse,fineMeasuredMetres=fine}));return 0;
    }
    internal static int CheckOutput(string bundle,string output,string noFade)
    {
        var d=CartographicMapData.Read(bundle);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"render-manifest.json")));var root=manifest.RootElement;
        Require(root.GetProperty("status").GetString()=="complete"&&root.GetProperty("format").GetString()==CartographicRenderer.Format,"Incomplete render contract.");
        Require(root.GetProperty("sourceManifestSha256").GetString()==d.ManifestHash,"Render source manifest mismatch.");
        foreach(var source in d.Hashes)Require(root.GetProperty("sourceLayers").GetProperty(source.Key).GetString()==source.Value,"Render source layer mismatch.");
        foreach(string name in new[]{"map.png","style-used.json","legend.json","legend.md"})Require(root.GetProperty("outputs").GetProperty(name).GetString()==CartographicMapData.Hash(Path.Combine(output,name)),"Published output hash mismatch.");
        Require(root.GetProperty("styleSha256").GetString()==CartographicMapData.Hash(Path.Combine(output,"style-used.json")),"Style hash mismatch.");
        Require(root.GetProperty("rendererSha256").GetString()==CartographicMapData.Hash(typeof(CartographicChecks).Assembly.Location),"Renderer hash mismatch.");
        Require(root.GetProperty("imageLibrarySha256").GetString()==CartographicMapData.Hash(typeof(Image).Assembly.Location),"Image library hash mismatch.");
        using var actual=Image.Load<Rgba32>(Path.Combine(output,"map.png"));using var sharp=Image.Load<Rgba32>(Path.Combine(noFade,"map.png"));
        Require(actual.Width==d.Size&&actual.Height==d.Size&&sharp.Width==d.Size&&sharp.Height==d.Size,"Published image dimensions mismatch.");
        var rgba=new byte[d.Size*d.Size*4];actual.CopyPixelDataTo(rgba);
        Require(Convert.ToHexString(SHA256.HashData(rgba))==root.GetProperty("pixelSha256").GetString(),"Decoded RGBA hash mismatch.");
        int landChanged=0,waterPixels=0;
        for(int y=0;y<d.Size;y++)for(int x=0;x<d.Size;x++){
            bool equal=actual[x,y].Equals(sharp[x,y]);
            if(d.Water[y*d.Size+x]==1){waterPixels++;Require(equal,"Real ocean pixel changed with land fade.");}else if(!equal)landChanged++;
        }
        Require(landChanged>0,"Fade did not affect any real land pixel.");d.VerifyUnchanged(bundle);
        System.Console.WriteLine(JsonSerializer.Serialize(new{status="passed",waterPixelsExact=waterPixels,landPixelsChanged=landChanged,decodedPixelAndAllOutputHashesExact=true}));return 0;
    }
}
